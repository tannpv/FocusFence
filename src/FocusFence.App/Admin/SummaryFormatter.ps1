function Format-PolicySummary([string]$accountName, [string]$sid, [bool]$restrictNewApps, [bool]$blockUninstallers, [array]$blockedExecutables, [array]$packages, [array]$uninstallers, [array]$unresolved, [string]$root) {
    return @(
        "ACCOUNT: $accountName ($sid)",
        "Restrict new apps: $restrictNewApps", "Block registered uninstallers: $blockUninstallers",
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
}
