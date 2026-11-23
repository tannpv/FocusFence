function Get-ManagedUninstallers([string]$sid) {
    if (-not (Test-Path "Registry::HKEY_USERS\$sid")) { throw 'Sign into the standard account and switch back to the administrator account before inventorying its uninstallers.' }
    $profileKey = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\$sid"
    if (-not (Test-Path -LiteralPath $profileKey)) { throw 'Sign into the standard account once to create its Windows profile before inventorying uninstallers.' }
    $profile = Get-ItemProperty -LiteralPath $profileKey
    if (-not $profile.PSObject.Properties['ProfileImagePath'] -or [string]::IsNullOrWhiteSpace($profile.ProfileImagePath)) { throw 'The selected account has no usable Windows profile path. Sign into it before inventorying uninstallers.' }
    $profilePath = [Environment]::ExpandEnvironmentVariables($profile.ProfileImagePath)
    $uninstallers = @(); $unresolved = @()
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
    return [pscustomobject]@{ Uninstallers = $uninstallers; Unresolved = $unresolved }
}
