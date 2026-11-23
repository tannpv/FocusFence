function Assert-ValidAccountName([string]$accountName) {
    if ([string]::IsNullOrWhiteSpace($accountName) -or $accountName.IndexOfAny([char[]]@('\', '/', '"', "`r", "`n")) -ge 0) {
        throw 'Enter a local Windows username, without a computer or domain prefix.'
    }
    return $accountName.Trim()
}

function Assert-AdminPolicyEnvironment([string]$action, [string]$accountName) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator credentials are required.' }
    $accountName = Assert-ValidAccountName $accountName
    $account = try { Get-LocalUser -Name $accountName -ErrorAction Stop } catch { throw "Local user account '$accountName' does not exist." }
    $sid = $account.SID.Value
    $administrators = @(Get-LocalGroupMember -SID 'S-1-5-32-544' | ForEach-Object { $_.SID.Value })
    if ($action -ne 'Restore' -and (-not $account.Enabled -or $sid -in $administrators -or $sid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value)) { throw 'Select a different, enabled standard account. Administrator and current accounts are not supported.' }
    if ((Get-CimInstance Win32_ComputerSystem).PartOfDomain) { throw 'Domain-managed PCs require an administrator policy review outside this helper.' }
    if ($action -ne 'Restore') {
        foreach ($existingPolicy in @('HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers', "Registry::HKEY_USERS\$sid\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers")) {
            if (Test-ExistingSrpConfiguration -Path $existingPolicy) { throw "Existing Software Restriction Policy configuration was detected at $existingPolicy. Review it with an administrator before using this helper." }
        }
        if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\AppLocker' -ErrorAction Stop) { throw 'Existing MDM AppLocker configuration was detected. Review it with an administrator before using this helper.' }
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
    $journalPath = Join-Path $root 'state.json'; $policyPath = Join-Path $root 'policy.xml'
    Assert-ProtectedBackupPath $root
    foreach ($file in @($journalPath, $policyPath, ($journalPath + '.tmp'))) {
        if ((Test-Path -LiteralPath $file) -and ((Get-Item -LiteralPath $file -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A policy backup file is redirected. Refusing to write.' }
        Assert-ProtectedBackupPath $file
    }
    [xml]$current = Get-AppLockerPolicy -Local -Xml; [xml]$effective = Get-AppLockerPolicy -Effective -Xml
    $journal = if (Test-Path -LiteralPath $journalPath) { Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json } else { $null }
    if ($journal -and $journal.Active) {
        if ($journal.Sid -ne $sid) { throw 'Restore the currently managed account before selecting another account.' }
        if ((ConvertTo-PolicySignature $current) -ne (ConvertTo-PolicySignature ([xml]$journal.AppliedPolicy))) { throw "Windows policy changed outside FocusFence. Refusing to overwrite it. Backup: $journalPath" }
    } elseif ($current.SelectNodes('/AppLockerPolicy/RuleCollection/*').Count -gt 0) {
        throw 'Existing AppLocker rules were found. This helper will not replace or weaken them.'
    }
    if ((ConvertTo-PolicySignature $current) -ne (ConvertTo-PolicySignature $effective)) { throw 'Effective policy differs from local policy. External policy management must be reviewed first.' }
    return [pscustomobject]@{
        Lock = $lock; Sid = $sid; Root = $root
        JournalPath = $journalPath; PolicyPath = $policyPath
        Journal = $journal; Current = $current
    }
}
