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
. (Join-Path $PSScriptRoot 'PolicyDetection.ps1')
. (Join-Path $PSScriptRoot 'RequestParser.ps1')
. (Join-Path $PSScriptRoot 'AccountUi.ps1')
. (Join-Path $PSScriptRoot 'BackupSecurity.ps1')
. (Join-Path $PSScriptRoot 'AdminPreflight.ps1')
. (Join-Path $PSScriptRoot 'UninstallerInventory.ps1')
. (Join-Path $PSScriptRoot 'SummaryFormatter.ps1')

try {
    $envContext = Assert-AdminPolicyEnvironment -action $Action -accountName $AccountName
    $lock = $envContext.Lock; $sid = $envContext.Sid; $root = $envContext.Root
    $journalPath = $envContext.JournalPath; $policyPath = $envContext.PolicyPath
    $journal = $envContext.Journal; $current = $envContext.Current

    if ($Action -eq 'Restore') {
        if (-not $journal -or -not $journal.Active) { Show-Result 'No active FocusFence policy backup exists for this account.'; exit $ExitCodes.NoPolicyToRestore }
        if (-not (Confirm-Preview "Restore the saved policy for $AccountName ($sid)?`r`n`r`nThis removes FocusFence's installer and uninstaller restrictions. Other standard-account permissions still apply.`r`nBackup: $journalPath")) { exit $ExitCodes.Cancelled }
        Write-Policy $journal.OriginalPolicy $policyPath
        $journal.AppliedPolicy = Get-AppLockerPolicy -Local -Xml; Save-Journal $journal $journalPath
        Set-IdentityStartup $journal.ServiceStartMode
        $serviceNote = ''
        if ($journal.ServiceStatus -eq 'Stopped') {
            try { Stop-Service AppIDSvc } catch { $serviceNote = "`r`nWindows kept Application Identity running. Its original startup mode was restored; it can stop on restart." }
        }
        $journal.Active = $false; Save-Journal $journal $journalPath
        Show-Result ("Previous policy restored for $AccountName. Backup retained at $journalPath." + $serviceNote)
        if ($serviceNote) { exit $ExitCodes.RestoredServiceRunning }
        exit $ExitCodes.Restored
    }

    $blockedExecutables = @()
    if ($BlockedExecutablesBase64) {
        $blockedExecutables = @(ConvertFrom-BlockedExecutableRequest $BlockedExecutablesBase64 | Select-Object -Unique)
        foreach ($path in $blockedExecutables) {
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The individual app executable cannot be found: $path. Refresh the app list." }
            if ([IO.Path]::GetFullPath($path).StartsWith($env:SystemRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Individual app switches cannot target Windows system executables.' }
        }
    }
    if (-not $RestrictNewApps -and -not $BlockUninstallers -and $blockedExecutables.Count -eq 0) { throw 'Select a restriction or disable an app, or use Restore.' }
    $packages = @()
    if ($RestrictNewApps) {
        $packages = @(Get-AppxPackage -User $sid | Select-Object Name, Publisher -Unique)
        if ($packages.Count -eq 0) { throw 'No packaged-app inventory is available. Sign into the standard account once, then retry. Refusing to block all Windows packaged apps.' }
    }
    $inventory = if ($BlockUninstallers) { Get-ManagedUninstallers $sid } else { [pscustomobject]@{ Uninstallers = @(); Unresolved = @() } }
    $programFolders = @([Environment]::GetFolderPath('ProgramFiles'), [Environment]::GetFolderPath('ProgramFilesX86')) | Where-Object { $_ } | Select-Object -Unique
    $planned = New-ManagedPolicy -Sid $sid -WindowsDirectory $env:SystemRoot -ProgramDirectories $programFolders -Packages $packages -UninstallExecutables $inventory.Uninstallers -BlockedExecutables $blockedExecutables -RestrictNewApps ([bool]$RestrictNewApps) -BlockUninstallers ([bool]$BlockUninstallers)
    $planned.OuterXml | Set-Content -LiteralPath $policyPath -Encoding UTF8
    $null = Test-AppLockerPolicy -XmlPolicy $policyPath -Path (Join-Path $env:SystemRoot 'System32\notepad.exe') -User $sid
    $summary = Format-PolicySummary $AccountName $sid ([bool]$RestrictNewApps) ([bool]$BlockUninstallers) $blockedExecutables $packages $inventory.Uninstallers $inventory.Unresolved $root
    if (-not (Confirm-Preview $summary)) { exit $ExitCodes.Cancelled }
    if ((ConvertTo-PolicySignature ([xml](Get-AppLockerPolicy -Local -Xml))) -ne (ConvertTo-PolicySignature $current)) { throw 'Policy changed during review. Retry from the current policy.' }
    $freshAccount = try { Get-LocalUser -Name $AccountName -ErrorAction Stop } catch { throw 'Account was removed during review. Retry with an enabled standard account.' }
    if (-not $freshAccount.Enabled -or $freshAccount.SID.Value -ne $sid -or $sid -in @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { $_.SID.Value })) { throw 'Account changed during review. Retry with an enabled standard account.' }
    $service = Get-CimInstance Win32_Service -Filter "Name='AppIDSvc'"
    $original = if ($journal -and $journal.Active) { $journal.OriginalPolicy } else { $current.OuterXml }
    $originalMode = if ($journal -and $journal.Active) { $journal.ServiceStartMode } else { $service.StartMode }
    $originalStatus = if ($journal -and $journal.Active) { $journal.ServiceStatus } else { (Get-Service AppIDSvc).Status.ToString() }
    $newJournal = [pscustomobject]@{ Active = $true; Sid = $sid; OriginalPolicy = $original; AppliedPolicy = $planned.OuterXml; ServiceStartMode = $originalMode; ServiceStatus = $originalStatus }
    Save-Journal $newJournal $journalPath
    try {
        Set-IdentityStartup 'Auto'; Start-Service AppIDSvc; Set-AppLockerPolicy -XmlPolicy $policyPath
        $newJournal.AppliedPolicy = Get-AppLockerPolicy -Local -Xml
        if ((ConvertTo-PolicySignature ([xml]$newJournal.AppliedPolicy)) -ne (ConvertTo-PolicySignature $planned)) { throw 'Windows did not retain the expected local policy.' }
        if ((Get-Service AppIDSvc).Status -ne 'Running') { throw 'Application Identity is not running; enforcement cannot be confirmed.' }
        Save-Journal $newJournal $journalPath
    } catch {
        $failure = $_.Exception.Message
        try {
            Write-Policy $current.OuterXml $policyPath; Set-IdentityStartup $service.StartMode
            if ($journal) { Save-Journal $journal $journalPath } else { $newJournal.Active = $false; Save-Journal $newJournal $journalPath }
        } catch { throw "Apply failed: $failure. Rollback also failed: $($_.Exception.Message). Recovery backup: $journalPath" }
        throw "Apply failed and the previous policy and service startup mode were restored: $failure. Application Identity may remain running until restart."
    }
    Show-Result "Policy applied for $AccountName. Sign out of that account and sign back in before testing. Review AppLocker event logs for enforcement results. Use Restore in FocusFence to remove this policy. Backup: $journalPath"
    exit $ExitCodes.Applied
} catch {
    $operationError = $_
    try {
        $diagnosticDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FocusFence'
        [void][IO.Directory]::CreateDirectory($diagnosticDirectory)
        [pscustomobject]@{ Time = [DateTimeOffset]::Now.ToString('o'); Action = $Action; Account = $AccountName; Message = $operationError.Exception.Message; Location = $operationError.InvocationInfo.PositionMessage } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $diagnosticDirectory 'admin-last-error.json') -Encoding UTF8
    } catch { }
    [void][System.Windows.Forms.MessageBox]::Show($operationError.Exception.Message, 'FocusFence policy operation failed', 'OK', 'Error')
    exit 1
} finally {
    if (Get-Variable lock -ErrorAction SilentlyContinue) { $lock.Dispose() }
}
