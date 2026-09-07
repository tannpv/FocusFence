param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\policy-tests'), [string]$TargetAccount)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\src\FocusFence.App\Admin\PolicyBuilder.ps1')
$script:checks = 0
function Assert($condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:checks++; Write-Output "PASS: $message"
}
$sid = if ($TargetAccount) { (Get-LocalUser -Name $TargetAccount).SID.Value } else { [Security.Principal.WindowsIdentity]::GetCurrent().User.Value }
$windows = [Environment]::GetFolderPath('Windows')
$programs = @([Environment]::GetFolderPath('ProgramFiles'))
$packages = @([pscustomobject]@{ Name = 'Example.App'; Publisher = 'CN=Example & Company' })
$policy = New-ManagedPolicy -Sid $sid -WindowsDirectory $windows -ProgramDirectories $programs -Packages $packages -UninstallExecutables @('C:\Example\remove.exe') -RestrictNewApps $true -BlockUninstallers $true
Assert ($policy.SelectNodes('/AppLockerPolicy/RuleCollection').Count -eq 4) 'All required rule collections generated'
Assert (@($policy.SelectNodes('//*[@Action="Deny"]') | Where-Object { $_.UserOrGroupSid -ne $sid }).Count -eq 0) 'Deny rules target only the selected user SID'
Assert (@($policy.SelectNodes('/AppLockerPolicy/RuleCollection') | Where-Object { $_.SelectNodes('*[@Action="Allow" and @UserOrGroupSid="S-1-1-0"]').Count -ne 1 }).Count -eq 0) 'Unrestricted baseline preserved for other accounts'
Assert ($policy.SelectNodes('//FilePublisherRule[@Action="Deny"]/Exceptions/FilePublisherCondition').Count -eq 1) 'Existing package identity included as an exception'
Assert ($policy.OuterXml.Contains('&amp;')) 'Publisher attributes are XML escaped'
Assert ($policy.SelectNodes('//FilePathCondition[@Path="C:\Example\remove.exe"]').Count -eq 1) 'Registered uninstaller path receives a rule'
$removalOnly = New-ManagedPolicy -Sid $sid -WindowsDirectory $windows -ProgramDirectories $programs -UninstallExecutables @('C:\Example\remove.exe') -RestrictNewApps $false -BlockUninstallers $true
$individual = New-ManagedPolicy -Sid $sid -WindowsDirectory $windows -ProgramDirectories $programs -BlockedExecutables @('C:\Example\game.exe') -RestrictNewApps $false -BlockUninstallers $false
Assert ($individual.SelectNodes('//FilePathRule[@Action="Deny"]').Count -eq 1 -and $individual.SelectNodes('/AppLockerPolicy/RuleCollection[@Type="Msi"]').Count -eq 0) 'Individual app rules do not add broad installer restrictions'
Assert ($removalOnly.SelectNodes('/AppLockerPolicy/RuleCollection[@Type="Script"]').Count -eq 0) 'Removal-only option does not restrict script files'
Assert ($removalOnly.SelectNodes('/AppLockerPolicy/RuleCollection[@Type="Appx"]/*[@Action="Allow"]').Count -eq 1 -and $removalOnly.SelectNodes('/AppLockerPolicy/RuleCollection[@Type="Appx"]/*[@Action="Deny"]').Count -eq 0) 'Removal-only option preserves packaged apps with the required explicit allow rule'
Assert ((Get-UninstallerExecutable '"C:\Program Files\Example\remove.exe" /quiet' 'C:\Users\Test') -eq 'C:\Program Files\Example\remove.exe') 'Quoted uninstaller path parsed without arguments'
Assert ($null -eq (Get-UninstallerExecutable 'C:\Program Files\Example\remove.exe /quiet' 'C:\Users\Test')) 'Ambiguous unquoted paths are reported instead of guessed'
Assert ((Get-UninstallerExecutable '"%LOCALAPPDATA%\Example\remove.exe"' 'C:\Users\Target') -eq 'C:\Users\Target\AppData\Local\Example\remove.exe') 'Profile variables resolve for the target account'
Assert ((Get-UninstallerExecutable '"%localappdata%\Example\remove.exe"' 'C:\Users\Target') -eq 'C:\Users\Target\AppData\Local\Example\remove.exe') 'Lowercase profile variables never resolve against the administrator profile'
Assert ($null -eq (Get-UninstallerExecutable 'msiexec.exe /x {example}' 'C:\Users\Target')) 'Relative command paths are not guessed'
[xml]$copy = $policy.OuterXml
$copy.DocumentElement.AppendChild($copy.DocumentElement.FirstChild) | Out-Null
Assert ((ConvertTo-PolicySignature $policy) -eq (ConvertTo-PolicySignature $copy)) 'Policy comparison ignores collection ordering'
$copy.SelectSingleNode('//*[@Action="Deny"]').SetAttribute('Action', 'Allow')
Assert ((ConvertTo-PolicySignature $policy) -ne (ConvertTo-PolicySignature $copy)) 'Policy comparison detects externally changed enforcement rules'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$path = Join-Path $OutputDirectory 'candidate.xml'
$policy.Save($path)
$probe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'FocusFence.Probe\bin\Release\net8.0\FocusFence.Probe.exe'))
$decisions = @(Test-AppLockerPolicy -XmlPolicy $path -Path $probe -User $sid)
Assert ($decisions[0].PolicyDecision -eq 'Denied') 'Windows evaluates the selected user executable outside trusted folders as denied'
$otherUser = 'S-1-5-18'
$decisions = @(Test-AppLockerPolicy -XmlPolicy $path -Path $probe -User $otherUser)
Assert ($decisions[0].PolicyDecision -in @('Allowed', 'AllowedByDefault')) 'Windows evaluation keeps SYSTEM executable access allowed'
$decisions = @(Test-AppLockerPolicy -XmlPolicy $path -Path (Join-Path $windows 'System32\notepad.exe') -User $sid)
Assert ($decisions[0].PolicyDecision -eq 'Allowed') 'Windows evaluation preserves trusted Windows executable access'
if ($TargetAccount) {
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    if ($currentSid -ne $sid) {
        $decisions = @(Test-AppLockerPolicy -XmlPolicy $path -Path $probe -User $currentSid)
        Assert ($decisions[0].PolicyDecision -eq 'Allowed') 'Windows evaluation leaves the operator account unrestricted'
    }
}
Write-Output "All $script:checks policy checks passed. No Windows policies were applied."
