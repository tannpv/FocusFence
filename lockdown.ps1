# lockdown.ps1 - Block software installs for the standard (kid) account "tannpv"
# Must be run elevated (as Administrator). Reversible via undo-lockdown.ps1
# Affects ONLY non-administrator users; the admin account (tannp) is untouched.

$ErrorActionPreference = 'Stop'
$KidUser = 'tannpv'
$log = Join-Path $PSScriptRoot 'lockdown-log.txt'
"==== Lockdown run $(Get-Date) ====" | Out-File $log
function Log($m){ $line = "[{0}] {1}" -f (Get-Date -Format HH:mm:ss), $m; Write-Host $line; $line | Out-File $log -Append }

# Confirm elevation
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Log 'ERROR: not running elevated. Aborting.'; exit 1 }
Log 'Running elevated. OK.'

# ---- 1) UAC: auto-deny elevation for standard users ----
# 0 = Automatically deny elevation requests (standard users cannot elevate at all)
try {
    $sysPol = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
    $old = (Get-ItemProperty $sysPol -Name ConsentPromptBehaviorUser -ErrorAction SilentlyContinue).ConsentPromptBehaviorUser
    Set-ItemProperty $sysPol -Name ConsentPromptBehaviorUser -Value 0 -Type DWord
    Log "UAC ConsentPromptBehaviorUser: $old -> 0 (standard users can no longer elevate, even with a password)"
} catch { Log "UAC step failed: $($_.Exception.Message)" }

# ---- 2) Software Restriction Policy: block execution from the kid's writable folders ----
# Scoped to non-admins (PolicyScope=1), default Unrestricted, with Disallowed path rules.
try {
    $sid = (Get-LocalUser -Name $KidUser).SID.Value
    $profilePath = (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid" -ErrorAction SilentlyContinue).ProfileImagePath
    if ([string]::IsNullOrWhiteSpace($profilePath)) { $profilePath = "C:\Users\$KidUser" }
    Log "Kid profile path: $profilePath"

    $base = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers'
    if (-not (Test-Path $base)) { New-Item $base -Force | Out-Null }
    Set-ItemProperty $base -Name DefaultLevel        -Value 0x40000 -Type DWord  # 262144 = Unrestricted (default allow)
    Set-ItemProperty $base -Name TransparentEnabled  -Value 1       -Type DWord  # enforce on executables (not DLLs)
    Set-ItemProperty $base -Name PolicyScope         -Value 1       -Type DWord  # skip local administrators
    if (-not (Get-ItemProperty $base -Name ExecutableTypes -ErrorAction SilentlyContinue)) {
        $exts = 'ADE','ADP','BAS','BAT','CHM','CMD','COM','CPL','CRT','EXE','HLP','HTA','INF','INS','ISP','LNK','MDB','MDE','MSC','MSI','MSP','MST','OCX','PCD','PIF','REG','SCR','SHS','URL','VB','WSC'
        New-ItemProperty $base -Name ExecutableTypes -Value $exts -PropertyType MultiString -Force | Out-Null
    }

    $disallowedRoot = Join-Path $base '0\Paths'
    if (-not (Test-Path $disallowedRoot)) { New-Item $disallowedRoot -Force | Out-Null }

    $blockFolders = @(
        "$profilePath\Downloads",
        "$profilePath\Desktop",
        "$profilePath\AppData\Roaming",
        "$profilePath\AppData\Local\Temp",
        "$profilePath\AppData\Local\Programs"
    )
    foreach ($f in $blockFolders) {
        $rule = "$f\*"
        # skip if an identical rule already exists
        $exists = $false
        Get-ChildItem $disallowedRoot -ErrorAction SilentlyContinue | ForEach-Object {
            $d = (Get-ItemProperty $_.PSPath -Name ItemData -ErrorAction SilentlyContinue).ItemData
            if ($d -eq $rule) { $exists = $true }
        }
        if ($exists) { Log "SRP rule already present: $rule"; continue }
        $g = [guid]::NewGuid().ToString('B')
        $k = Join-Path $disallowedRoot $g
        New-Item $k -Force | Out-Null
        New-ItemProperty $k -Name ItemData    -Value $rule -PropertyType ExpandString -Force | Out-Null
        New-ItemProperty $k -Name SaferFlags  -Value 0     -PropertyType DWord        -Force | Out-Null
        New-ItemProperty $k -Name Description -Value 'Block installers/executables from kid-writable folder' -PropertyType String -Force | Out-Null
        Log "SRP Disallowed rule added: $rule"
    }
} catch { Log "SRP step failed: $($_.Exception.Message)" }

# ---- 3) Ensure auto-logon is off ----
try {
    $wl = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    Set-ItemProperty $wl -Name AutoAdminLogon -Value '0' -Type String
    Log 'Auto-logon disabled (AutoAdminLogon=0)'
} catch { Log "Auto-logon step failed: $($_.Exception.Message)" }

# ---- Apply policy ----
try { gpupdate /target:computer /force | Out-Null; Log 'gpupdate applied' } catch { Log "gpupdate note: $($_.Exception.Message)" }

Log 'DONE. Changes take full effect at the next logon of the standard account.'
Log "Reminder: set a strong password on the admin account '$env:USERNAME' that the kid does not know."
