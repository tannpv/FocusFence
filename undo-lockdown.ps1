# undo-lockdown.ps1 - Reverts everything lockdown.ps1 changed. Run elevated.
$ErrorActionPreference = 'Continue'
function Log($m){ Write-Host ("[{0}] {1}" -f (Get-Date -Format HH:mm:ss), $m) }

# 1) Restore UAC default (3 = prompt standard users for credentials)
Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name ConsentPromptBehaviorUser -Value 3 -Type DWord
Log 'UAC ConsentPromptBehaviorUser restored to 3 (default).'

# 2) Remove the Software Restriction Policy rules we added (and the disallowed level)
$base = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Safer\CodeIdentifiers'
$disallowedRoot = Join-Path $base '0'
if (Test-Path $disallowedRoot) { Remove-Item $disallowedRoot -Recurse -Force; Log 'Removed SRP Disallowed rules.' }
# Reset scope to all users (0) so no SRP scoping remains
if (Test-Path $base) { Set-ItemProperty $base -Name PolicyScope -Value 0 -Type DWord -ErrorAction SilentlyContinue }

gpupdate /target:computer /force | Out-Null
Log 'DONE. Reverted. Effective at next logon.'
