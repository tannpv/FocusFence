# Pure policy construction. Dot-sourcing this file does not read or change Windows policy.
Set-StrictMode -Version Latest

function New-PolicyElement {
    param([xml]$Document, [string]$Name, [hashtable]$Attributes = @{})
    $node = $Document.CreateElement($Name)
    foreach ($key in $Attributes.Keys) { $node.SetAttribute($key, [string]$Attributes[$key]) }
    return ,$node
}

function Add-PathPolicyRule {
    param([xml]$Document, $Collection, [string]$Sid, [string]$Name, [string]$Action, [string]$Path, [string[]]$Exceptions = @())
    $rule = New-PolicyElement $Document 'FilePathRule' @{ Id = [guid]::NewGuid(); Name = $Name; Description = 'Managed by FocusFence'; UserOrGroupSid = $Sid; Action = $Action }
    $conditions = New-PolicyElement $Document 'Conditions'
    [void]$conditions.AppendChild((New-PolicyElement $Document 'FilePathCondition' @{ Path = $Path }))
    [void]$rule.AppendChild($conditions)
    if ($Exceptions.Count -gt 0) {
        $except = New-PolicyElement $Document 'Exceptions'
        foreach ($pathException in $Exceptions) { [void]$except.AppendChild((New-PolicyElement $Document 'FilePathCondition' @{ Path = $pathException })) }
        [void]$rule.AppendChild($except)
    }
    [void]$Collection.AppendChild($rule)
}

function New-PublisherCondition {
    param([xml]$Document, [string]$Publisher, [string]$Product)
    $condition = New-PolicyElement $Document 'FilePublisherCondition' @{ PublisherName = $Publisher; ProductName = $Product; BinaryName = '*' }
    [void]$condition.AppendChild((New-PolicyElement $Document 'BinaryVersionRange' @{ LowSection = '0.0.0.0'; HighSection = '*' }))
    return ,$condition
}

function New-ManagedPolicy {
    param([string]$Sid, [string]$WindowsDirectory, [string[]]$ProgramDirectories,
        [object[]]$Packages = @(), [string[]]$UninstallExecutables = @(), [string[]]$BlockedExecutables = @(),
        [bool]$RestrictNewApps, [bool]$BlockUninstallers)
    if ($Sid -notmatch '^S-1-5-21-\d+-\d+-\d+-\d+$') { throw 'A local user SID is required.' }
    if (-not $RestrictNewApps -and -not $BlockUninstallers -and $BlockedExecutables.Count -eq 0) { throw 'Choose a restriction or restore the previous policy.' }
    [xml]$document = '<AppLockerPolicy Version="1" />'
    $exe = New-PolicyElement $document 'RuleCollection' @{ Type = 'Exe'; EnforcementMode = 'Enabled' }
    [void]$document.DocumentElement.AppendChild($exe)
    Add-PathPolicyRule $document $exe 'S-1-1-0' 'Keep other executable access unchanged' 'Allow' '*'
    if ($RestrictNewApps) {
        $trusted = @($WindowsDirectory) + $ProgramDirectories | Select-Object -Unique
        $exceptions = @($trusted | ForEach-Object { $_.TrimEnd('\') + '\*' })
        Add-PathPolicyRule $document $exe $Sid 'Restrict executables to Windows and Program Files' 'Deny' '*' $exceptions
        Add-PathPolicyRule $document $exe $Sid 'Block Windows temporary executables' 'Deny' ($WindowsDirectory.TrimEnd('\') + '\Temp\*')
        $scripts = New-PolicyElement $document 'RuleCollection' @{ Type = 'Script'; EnforcementMode = 'Enabled' }
        [void]$document.DocumentElement.AppendChild($scripts)
        Add-PathPolicyRule $document $scripts 'S-1-1-0' 'Keep other script access unchanged' 'Allow' '*'
        Add-PathPolicyRule $document $scripts $Sid 'Block script files for managed account' 'Deny' '*'
    }
    # EXE enforcement requires an Appx collection even for removal-only policies.
    # Without it, Windows can block packaged apps for every account by default.
        $appx = New-PolicyElement $document 'RuleCollection' @{ Type = 'Appx'; EnforcementMode = 'Enabled' }
        [void]$document.DocumentElement.AppendChild($appx)
        $actions = if ($RestrictNewApps) { @('Allow', 'Deny') } else { @('Allow') }
        foreach ($action in $actions) {
            $rule = New-PolicyElement $document 'FilePublisherRule' @{ Id = [guid]::NewGuid(); Name = "$action packaged apps"; Description = 'Managed by FocusFence'; UserOrGroupSid = $(if ($action -eq 'Allow') { 'S-1-1-0' } else { $Sid }); Action = $action }
            $conditions = New-PolicyElement $document 'Conditions'
            [void]$conditions.AppendChild((New-PublisherCondition $document '*' '*'))
            [void]$rule.AppendChild($conditions)
            if ($action -eq 'Deny' -and $Packages.Count -gt 0) {
                $exceptions = New-PolicyElement $document 'Exceptions'
                foreach ($package in $Packages) { [void]$exceptions.AppendChild((New-PublisherCondition $document $package.Publisher $package.Name)) }
                [void]$rule.AppendChild($exceptions)
            }
            [void]$appx.AppendChild($rule)
        }
    # MSI installation and maintenance share the same engine; the preview explains this coupling.
    if ($RestrictNewApps -or $BlockUninstallers) {
      foreach ($folder in @('System32', 'SysWOW64')) {
        Add-PathPolicyRule $document $exe $Sid 'Block Windows Installer launcher' 'Deny' (Join-Path $WindowsDirectory "$folder\msiexec.exe")
    }
    $msi = New-PolicyElement $document 'RuleCollection' @{ Type = 'Msi'; EnforcementMode = 'Enabled' }
    [void]$document.DocumentElement.AppendChild($msi)
    Add-PathPolicyRule $document $msi 'S-1-1-0' 'Keep other installer access unchanged' 'Allow' '*'
    Add-PathPolicyRule $document $msi $Sid 'Block Windows Installer packages' 'Deny' '*'
    }
    foreach ($path in ($BlockedExecutables | Select-Object -Unique)) {
        Add-PathPolicyRule $document $exe $Sid 'Individually disabled application' 'Deny' $path
    }
    if ($BlockUninstallers) {
        foreach ($path in ($UninstallExecutables | Select-Object -Unique)) {
            Add-PathPolicyRule $document $exe $Sid 'Block registered uninstaller executable' 'Deny' $path
        }
    }
    return ,$document
}

function ConvertTo-PolicySignature {
    param([xml]$Document)
    function Normalize-Node($node) {
        $attributes = @($node.Attributes | Sort-Object Name | ForEach-Object { $_.Name + '=' + [System.Security.SecurityElement]::Escape($_.Value) }) -join '|'
        $children = @($node.ChildNodes | Where-Object { $_ -is [System.Xml.XmlElement] } | ForEach-Object { Normalize-Node $_ } | Sort-Object) -join ''
        return '<' + $node.Name + ' ' + $attributes + '>' + $children + '</' + $node.Name + '>'
    }
    return Normalize-Node $Document.DocumentElement
}

function Get-UninstallerExecutable {
    param([string]$Command, [string]$ProfilePath)
    if ([string]::IsNullOrWhiteSpace($Command)) { return $null }
    $variables = @{ USERPROFILE = $ProfilePath; LOCALAPPDATA = (Join-Path $ProfilePath 'AppData\Local'); APPDATA = (Join-Path $ProfilePath 'AppData\Roaming') }
    $expanded = [regex]::Replace($Command, '(?i)%(USERPROFILE|LOCALAPPDATA|APPDATA)%', { param($match) $variables[$match.Groups[1].Value] })
    $expanded = [Environment]::ExpandEnvironmentVariables($expanded)
    if ($expanded -match '^\s*"([^"\r\n]+\.exe)"(?:\s|$)') { $path = $Matches[1] }
    elseif ($expanded -match '^\s*([^\s"]+\.exe)(?:\s|$)') { $path = $Matches[1] }
    else { return $null } # Ambiguous unquoted paths are reported, never guessed.
    if (-not [IO.Path]::IsPathRooted($path)) { return $null }
    return [IO.Path]::GetFullPath($path)
}
