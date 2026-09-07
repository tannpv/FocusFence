param(
    [ValidateSet('ReviewAndApply', 'Restore')][string]$Action,
    [Parameter(Mandatory = $true)][string]$AccountName,
    [switch]$RestrictNewApps,
    [switch]$BlockUninstallers,
    [string]$BlockedExecutablesBase64 = ''
)
$ErrorActionPreference = 'Stop'
# Shared with FocusFence.Core.ManagedPolicyOutcome; 0 is never a confirmed outcome.
$ExitCodes = @{ Applied = 10; Restored = 11; Cancelled = 12; NoPolicyToRestore = 13; RestoredServiceRunning = 14 }
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'PolicyBuilder.ps1')
Add-Type -AssemblyName System.Windows.Forms

function Show-Result([string]$message) {
    [void][System.Windows.Forms.MessageBox]::Show($message, 'FocusFence administrator controls', 'OK', 'Information')
}

function Confirm-Preview([string]$message) {
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Review FocusFence policy changes'
    $form.Size = New-Object System.Drawing.Size(820, 650)
    $form.StartPosition = 'CenterScreen'
    $text = New-Object System.Windows.Forms.TextBox
    $text.Multiline = $true; $text.ReadOnly = $true; $text.ScrollBars = 'Both'; $text.Dock = 'Fill'; $text.Text = $message
    $buttons = New-Object System.Windows.Forms.FlowLayoutPanel
    $buttons.Dock = 'Bottom'; $buttons.Height = 48
    $apply = New-Object System.Windows.Forms.Button
    $apply.Text = 'Apply'; $apply.Width = 120; $apply.DialogResult = 'OK'
    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'; $cancel.Width = 120; $cancel.DialogResult = 'Cancel'
    [void]$buttons.Controls.Add($apply); [void]$buttons.Controls.Add($cancel)
    [void]$form.Controls.Add($text); [void]$form.Controls.Add($buttons)
    $form.CancelButton = $cancel
    try { return $form.ShowDialog() -eq 'OK' } finally { $form.Dispose() }
}

function Save-Journal($journal, [string]$path) {
    $temporary = $path + '.tmp'
    $journal | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $path -Force
}

function Set-IdentityStartup([string]$mode) {
    $value = switch ($mode) { 'Auto' { 'auto' }; 'Automatic' { 'auto' }; 'Manual' { 'demand' }; 'Disabled' { 'disabled' }; default { throw "Unknown service mode: $mode" } }
    & (Join-Path $env:SystemRoot 'System32\sc.exe') config AppIDSvc start= $value | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not change Application Identity service startup.' }
}

function Write-Policy([string]$xml, [string]$path) {
    $xml | Set-Content -LiteralPath $path -Encoding UTF8
    Set-AppLockerPolicy -XmlPolicy $path
}

function Assert-ProtectedBackupPath([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Policy backup paths must not be redirected.' }
    $security = Get-Acl -LiteralPath $path
    foreach ($entry in $security.Access) {
        $entrySid = $entry.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value
        if ($entry.AccessControlType -eq 'Allow' -and $entrySid -notin @('S-1-5-32-544', 'S-1-5-18')) {
            throw 'Policy backup permissions are not restricted to Administrators and SYSTEM. Refusing to trust or overwrite these files.'
        }
    }
}

try {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator credentials are required.' }
    $account = Get-LocalUser -Name $AccountName
    $sid = $account.SID.Value
    $administrators = @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { $_.SID.Value })
    if ($Action -ne 'Restore' -and (-not $account.Enabled -or $sid -in $administrators -or $sid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)) { throw 'Select a different, enabled standard account. Administrator and current accounts are not supported.' }
    if ((Get-CimInstance Win32_ComputerSystem).PartOfDomain) { throw 'Domain-managed PCs require an administrator policy review outside this helper.' }
    if ($Action -ne 'Restore') {
        foreach ($existingPolicy in @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers', "Registry::HKEY_USERS\$sid\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers", 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\AppLocker')) {
            if (Test-Path -LiteralPath $existingPolicy) { throw 'Existing Software Restriction Policy or MDM AppLocker configuration was detected. Review it with an administrator before using this helper.' }
        }
    }
    $lock = New-Object System.Threading.Mutex($false, 'Global\FocusFence.ManagedPolicy')
    if (-not $lock.WaitOne(0)) { throw 'Another policy operation is in progress.' }
    $root = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'FocusFence-AdminPolicy'
    if (Test-Path -LiteralPath $root) {
        if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The policy backup directory must not be a redirected path.' }
        $existingAcl = Get-Acl -LiteralPath $root
        $ownerSid = $existingAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        if ($ownerSid -notin @('S-1-5-32-544', 'S-1-5-18')) { throw 'Policy backup directory has an unexpected owner. An administrator must inspect it.' }
    } else {
        $acl = New-Object Security.AccessControl.DirectorySecurity
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
        foreach ($owner in @('S-1-5-32-544', 'S-1-5-18')) {
            $rule = New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($owner)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.AddAccessRule($rule)
        }
        [void][IO.Directory]::CreateDirectory($root, $acl)
    }
    $journalPath = Join-Path $root 'state.json'
    $policyPath = Join-Path $root 'policy.xml'
    Assert-ProtectedBackupPath $root
    foreach ($file in @($journalPath, $policyPath, ($journalPath + '.tmp'))) {
        if ((Test-Path -LiteralPath $file) -and ((Get-Item -LiteralPath $file -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A policy backup file is redirected. Refusing to write.' }
        Assert-ProtectedBackupPath $file
    }
    [xml]$current = Get-AppLockerPolicy -Local -Xml
    [xml]$effective = Get-AppLockerPolicy -Effective -Xml
    $journal = if (Test-Path -LiteralPath $journalPath) { Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json } else { $null }
    if ($journal -and $journal.Active) {
        if ($journal.Sid -ne $sid) { throw 'Restore the currently managed account before selecting another account.' }
        if ((ConvertTo-PolicySignature $current) -ne (ConvertTo-PolicySignature ([xml]$journal.AppliedPolicy))) { throw "Windows policy changed outside FocusFence. Refusing to overwrite it. Backup: $journalPath" }
    } elseif ($current.SelectNodes('/AppLockerPolicy/RuleCollection/*').Count -gt 0) {
        throw 'Existing AppLocker rules were found. This helper will not replace or weaken them.'
    }
    if ((ConvertTo-PolicySignature $current) -ne (ConvertTo-PolicySignature $effective)) { throw 'Effective policy differs from local policy. External policy management must be reviewed first.' }

    if ($Action -eq 'Restore') {
        if (-not $journal -or -not $journal.Active) { Show-Result 'No active FocusFence policy backup exists for this account.'; exit $ExitCodes.NoPolicyToRestore }
        if (-not (Confirm-Preview "Restore the saved policy for $AccountName ($sid)?`r`n`r`nThis removes FocusFence's installer and uninstaller restrictions. Other standard-account permissions still apply.`r`nBackup: $journalPath")) { exit $ExitCodes.Cancelled }
        Write-Policy $journal.OriginalPolicy $policyPath
        $journal.AppliedPolicy = Get-AppLockerPolicy -Local -Xml
        Save-Journal $journal $journalPath
        Set-IdentityStartup $journal.ServiceStartMode
        $serviceNote = ''
        if ($journal.ServiceStatus -eq 'Stopped') {
            try { Stop-Service AppIDSvc } catch { $serviceNote = "`r`nWindows kept Application Identity running. Its original startup mode was restored; it can stop on restart." }
        }
        $journal.Active = $false
        Save-Journal $journal $journalPath
        Show-Result ("Previous policy restored for $AccountName. Backup retained at $journalPath." + $serviceNote)
        if ($serviceNote) { exit $ExitCodes.RestoredServiceRunning }
        exit $ExitCodes.Restored
    }

    $blockedExecutables = @()
    if ($BlockedExecutablesBase64) {
        if ($BlockedExecutablesBase64.Length -gt 20000) { throw 'The individual app request is too large.' }
        $blockedExecutables = @([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($BlockedExecutablesBase64)) | ConvertFrom-Json)
        if ($blockedExecutables.Count -gt 100) { throw 'Select at most 100 individual apps.' }
        foreach ($path in $blockedExecutables) {
            if ($path -isnot [string] -or -not [IO.Path]::IsPathRooted($path) -or $path -notmatch '\.exe$' -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'An individual app path is invalid or no longer exists. Refresh the app list.' }
            if ([IO.Path]::GetFullPath($path).StartsWith($env:SystemRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Individual app switches cannot target Windows system executables.' }
        }
    }
    if (-not $RestrictNewApps -and -not $BlockUninstallers -and $blockedExecutables.Count -eq 0) { throw 'Select a restriction or disable an app, or use Restore.' }
    $profile = Get-ItemProperty -LiteralPath "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
    $profilePath = [Environment]::ExpandEnvironmentVariables($profile.ProfileImagePath)
    $packages = @()
    if ($RestrictNewApps) {
        $packages = @(Get-AppxPackage -User $sid | Select-Object Name, Publisher -Unique)
        if ($packages.Count -eq 0) { throw 'No packaged-app inventory is available. Sign into the standard account once, then retry. Refusing to block all Windows packaged apps.' }
    }
    $uninstallers = @(); $unresolved = @()
    if ($BlockUninstallers) {
        if (-not (Test-Path "Registry::HKEY_USERS\$sid")) { throw 'Sign into the standard account and switch back to the administrator account before inventorying its uninstallers.' }
        $locations = @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall', "Registry::HKEY_USERS\$sid\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        foreach ($location in $locations) {
            if (-not (Test-Path -LiteralPath $location)) { continue }
            foreach ($key in Get-ChildItem -LiteralPath $location) {
                $entry = Get-ItemProperty -LiteralPath $key.PSPath
                foreach ($property in @('UninstallString', 'QuietUninstallString')) {
                    if (-not $entry.PSObject.Properties[$property]) { continue }
                    $path = Get-UninstallerExecutable $entry.$property $profilePath
                    if ($path -and (Test-Path -LiteralPath $path -PathType Leaf)) { $uninstallers += $path }
                    else { $unresolved += "$($key.PSChildName): $($entry.$property)" }
                }
            }
        }
    }
    $programFolders = @([Environment]::GetFolderPath('ProgramFiles'), [Environment]::GetFolderPath('ProgramFilesX86')) | Where-Object { $_ } | Select-Object -Unique
    $planned = New-ManagedPolicy -Sid $sid -WindowsDirectory $env:SystemRoot -ProgramDirectories $programFolders -Packages $packages -UninstallExecutables $uninstallers -BlockedExecutables $blockedExecutables -RestrictNewApps ([bool]$RestrictNewApps) -BlockUninstallers ([bool]$BlockUninstallers)
    $planned.OuterXml | Set-Content -LiteralPath $policyPath -Encoding UTF8
    # This parses and evaluates a candidate policy without applying it.
    $null = Test-AppLockerPolicy -XmlPolicy $policyPath -Path (Join-Path $env:SystemRoot 'System32\notepad.exe') -User $sid
    $summary = @(
        "ACCOUNT: $AccountName ($sid)",
        "Restrict new apps: $([bool]$RestrictNewApps)", "Block registered uninstallers: $([bool]$BlockUninstallers)",
        '', 'Either broad installer/uninstaller option blocks Windows Installer packages and msiexec, including installs, repairs and removals. Individual-app-only rules do not block MSI.',
        '', 'INDIVIDUALLY DISABLED EXECUTABLES:', $blockedExecutables,
        'Enabling an app removes its individual deny rule only after you reapply. Other rules may still restrict it. Per-app rules match executable paths, not every component of a product.',
        'New-app restriction also blocks EXE/COM files outside Windows/Program Files, script files, and package identities not already installed for this user.',
        'Existing per-user desktop apps may stop working. Existing packaged-app identities (including updates/reinstalls) remain allowed.',
        'Registered uninstaller paths are blocked as entire executables, not just their uninstall arguments. Shared executables may also stop working as normal apps.',
        '', 'LIMITS: AppLocker is not a security boundary. User-owned files can still be deleted. Store-app removal and alternative/unregistered uninstall routes are not universally blocked.',
        'Built-in interpreters, allowed-folder programs, administrator credentials, and writable subfolders under trusted folders can provide bypasses.',
        'This helper is for standalone, unmanaged PCs. Do not use it on MDM-managed devices.',
        '', 'Application Identity will be started and set to automatic. Other accounts receive no deny rules.',
        "Backups: $root", '', "APPROVED PACKAGE IDENTITIES ($($packages.Count)):",
        ($packages | ForEach-Object { $_.Name }), '', 'BLOCKED REGISTERED UNINSTALLER EXECUTABLES:',
        ($uninstallers | Select-Object -Unique), '', 'UNRESOLVED UNINSTALL COMMANDS (not specifically covered):', $unresolved
    ) -join "`r`n"
    if (-not (Confirm-Preview $summary)) { exit $ExitCodes.Cancelled }
    # Recheck after the potentially long review dialog.
    if ((ConvertTo-PolicySignature ([xml](Get-AppLockerPolicy -Local -Xml))) -ne (ConvertTo-PolicySignature $current)) { throw 'Policy changed during review. Retry from the current policy.' }
    $freshAccount = Get-LocalUser -Name $AccountName
    if (-not $freshAccount.Enabled -or $freshAccount.SID.Value -ne $sid -or $sid -in @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { $_.SID.Value })) { throw 'Account changed during review. Retry with an enabled standard account.' }
    $service = Get-CimInstance Win32_Service -Filter "Name='AppIDSvc'"
    $original = if ($journal -and $journal.Active) { $journal.OriginalPolicy } else { $current.OuterXml }
    $originalMode = if ($journal -and $journal.Active) { $journal.ServiceStartMode } else { $service.StartMode }
    $originalStatus = if ($journal -and $journal.Active) { $journal.ServiceStatus } else { (Get-Service AppIDSvc).Status.ToString() }
    $newJournal = [pscustomobject]@{ Active = $true; Sid = $sid; OriginalPolicy = $original; AppliedPolicy = $planned.OuterXml; ServiceStartMode = $originalMode; ServiceStatus = $originalStatus }
    Save-Journal $newJournal $journalPath
    try {
        Set-IdentityStartup 'Auto'
        Start-Service AppIDSvc
        Set-AppLockerPolicy -XmlPolicy $policyPath
        $newJournal.AppliedPolicy = Get-AppLockerPolicy -Local -Xml
        if ((ConvertTo-PolicySignature ([xml]$newJournal.AppliedPolicy)) -ne (ConvertTo-PolicySignature $planned)) { throw 'Windows did not retain the expected local policy.' }
        if ((Get-Service AppIDSvc).Status -ne 'Running') { throw 'Application Identity is not running; enforcement cannot be confirmed.' }
        Save-Journal $newJournal $journalPath
    } catch {
        $failure = $_.Exception.Message
        try {
            Write-Policy $current.OuterXml $policyPath
            Set-IdentityStartup $service.StartMode
            if ($journal) { Save-Journal $journal $journalPath }
            else { $newJournal.Active = $false; Save-Journal $newJournal $journalPath }
        } catch { throw "Apply failed: $failure. Rollback also failed: $($_.Exception.Message). Recovery backup: $journalPath" }
        throw "Apply failed and the previous policy and service startup mode were restored: $failure. Application Identity may remain running until restart."
    }
    Show-Result "Policy applied for $AccountName. Sign out of that account and sign back in before testing. Review AppLocker event logs for enforcement results. Use Restore in FocusFence to remove this policy. Backup: $journalPath"
    exit $ExitCodes.Applied
} catch {
    [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'FocusFence policy operation failed', 'OK', 'Error')
    exit 1
} finally {
    if (Get-Variable lock -ErrorAction SilentlyContinue) { $lock.Dispose() }
}
