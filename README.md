# FocusFence

A local Windows focus app. Create executable blocklists, start timed sessions, and schedule recurring focus hours. Selected processes are terminated when detected; save work before starting. Close the window to keep blocking in the tray, or use tray → Exit to quit.

## Run

Download the Windows ZIP from [GitHub Releases](https://github.com/tannpv/FocusFence/releases/latest), extract the entire ZIP to a folder, and open `FocusFence.App.exe`. The repository and its downloads are private. Close an existing FocusFence instance from its tray menu before replacing its application files. Settings and PIN remain in `%LOCALAPPDATA%\FocusFence`.

In **Apps**, select **My current account** to close disabled executables while FocusFence is running, including when minimized to the tray. Enabling an app removes it from that monitoring list. The managed-account list is separate and requires administrator review and application before Windows enforces it. Current-account blocking does not persist after exiting FocusFence and can be bypassed by an administrator.

Requires Windows and the .NET 8 Desktop Runtime (the installed .NET 8 SDK also works).

```powershell
dotnet run --project src/FocusFence.App
```

## Build and verify

```powershell
dotnet build src/FocusFence.App -c Release
dotnet build tests/FocusFence.Probe -c Release
dotnet run --project tests/FocusFence.Tests -c Release -- tests/FocusFence.Probe/bin/Release/net8.0/FocusFence.Probe.exe
dotnet run --project tests/FocusFence.UiSmoke -c Release -- artifacts/ui-smoke.png
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Test-ManagedPolicy.ps1
dotnet publish src/FocusFence.App -c Release -o artifacts/FocusFence
```

Launch `artifacts/FocusFence/FocusFence.App.exe`. Keep the complete output folder together. This is a framework-dependent portable build, not an installer.

## Design

- `FocusFence.Core`: session transitions, calendar schedule evaluation, configurable defaults, data models, and infrastructure interfaces. TimeProvider makes deadline behavior testable without waiting.
- `FocusFence.Windows`: executable safety policy, current-session process monitoring, and atomic JSON settings replacement.
- `FocusFence.App`: WPF presentation, tray integration, composition, and user interaction. Blocking runs off the UI thread; checks cannot overlap.
- `tests`: dependency-free executable checks with a disposable, harmless process for integration testing. Tests never terminate the user's apps.

Settings live at `%LOCALAPPDATA%/FocusFence/settings.json`. Blocklists and durations are editable; session targets are snapshotted at start. Session expiry uses UTC deadlines, including across sleep and restart. Reopening asks before resuming an unexpired session. Corrupt settings cause a visible startup error and are preserved for recovery.

## Individual app controls

Open **Apps** to see desktop executables discovered from running apps, registered App Paths, and Start menu shortcuts visible to the current Windows account. Search by name or path, or browse for an executable installed only for the managed user. Windows/system executables are shown as protected. Packaged Store apps without desktop executable entry points are not included in this list.

Each row has **Disable** or **Enable**. These save the desired app-access draft; they do not immediately change Windows policy. Open **Managed account**, enter the standard-account username, and choose **Review and apply as administrator** to apply that draft alongside the selected installer/uninstaller options. Review all options on each application: the preview represents the complete replacement for FocusFence's previous managed policy.

The app-list footer shows the saved account and total disabled-app count, including entries outside the current search. **Review account draft** opens the managed-account tab directly. **Save draft** remembers the username and broad policy options without changing Windows policy. Review-and-apply also saves these choices before opening the administrator helper; a storage failure stops the launch. Saved choices return when FocusFence restarts. They describe the desired configuration, not verified live enforcement.

Individual-only rules do not block MSI, scripts, or other desktop apps. Enabling removes that executable's individual deny rule when reapplied; broader existing rules can still block it. To clear the final restriction when no other options remain, use **Restore previous policy**. A product may have multiple executables: each relevant executable needs its own entry. Draft state is not a live report of Windows enforcement, and an administrator can cancel application without changing policy.

## PIN authentication

Click **Set PIN**, or **Settings → Set / change PIN**, to create a 6–12 digit FocusFence PIN. There is no default PIN. Existing installations remain unlocked until one is configured. Restarting, closing to the tray, manually locking, or locking the Windows session locks the UI. Use **Sign in with PIN** to regain access. The PIN also gates tray Exit and emergency unlock; background blocking continues while locked. Configured-PIN launches resume active blocking without offering an unauthenticated skip dialog.

PINs use a random salt and PBKDF2-HMAC-SHA256 with 600,000 iterations, following [OWASP's PBKDF2 guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html). The app stores only the hash and salt, uses constant-time hash comparison, and persists a one-minute cooldown after five failed attempts. Changing a configured PIN first requires the current PIN. Storage failures do not grant access.

This is a local application lock, not Windows Hello or a substitute for Windows administrator authentication. The PIN/settings belong to the Windows account running FocusFence. Someone who can edit that account's settings file can bypass the local lock; managed-account Windows policies still require administrator access to change. There is no in-app forgotten-PIN bypass. Keep a settings backup; an account owner can deliberately reset the local profile if necessary, which does not remove managed Windows policies. PIN protection is not a tamper-proof child-account security boundary.

## Recurring schedules

Create a blocklist, then open **Schedules**. Enter a name, select the blocklist and start weekdays, and enter 24-hour start/end times. Check **Enabled** and save to enable automatic blocking; the app confirms that apps may be closed immediately. Unchecked schedules are saved as inactive drafts. Existing settings gain an empty schedule list automatically.

- An end time earlier than the start runs overnight. Monday 22:00–06:00 ends on Tuesday morning. Equal start/end times are rejected.
- A schedule keeps the Windows time zone in effect when it was created, shown in the editor. It does not silently switch zones when you travel. Recreate it to use a different local zone.
- Overlapping schedules and manual sessions combine their blocked apps. The timer shows the next ending period; apps needed by another active period remain blocked.
- Emergency unlock ends the manual session and skips all currently active scheduled periods. Skips persist after restarting; future periods still run. Declining the startup resume prompt has the same effect.
- Schedule and blocklist edits are blocked during active focus. Delete or reassign a schedule before deleting its blocklist. Empty blocklists do not cause blocking.
- After sleep, the app evaluates the current time; it does not replay missed periods. Spring daylight-saving gaps advance to the first valid minute; fall repeated times use the earliest start and latest end.
- Keep FocusFence running in the tray. Schedules cannot block when the app is closed; optional sign-in startup is available in Settings.

## Start at sign-in

In **Settings**, check **Start FocusFence at Windows sign-in** and click **Apply startup preference**. It is off by default. This registers only FocusFence's entry under the current user's Windows Run key; administrator access is not required. Uncheck and apply to remove the entry.

Sign-in launches in the tray and resumes active manual sessions and scheduled periods without asking again. Normal launches still show the resume prompt. Keep the published application folder in a permanent location before enabling this option. If you move it, open the new copy and apply the preference again. Disable startup before deleting the app folder.

Windows may delay startup, and startup disabled through Windows Settings or Task Manager may need to be enabled there too. The app displays its own registration, not Windows' separate startup override. This is not a guarantee of blocking immediately at sign-in. See [Microsoft's Run key documentation](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys).

Startup tests use fake registration (including access-denied failures) and a hidden WPF window with fake process monitoring. They do not modify actual sign-in preferences or perform a real sign-out/sign-in cycle.

Verification includes calendar boundaries, overnight rules, overlapping/manual sessions, skipped periods, persistence compatibility, daylight-saving transitions, process integration, and rendering all six tabs. The UI smoke check also saves a disabled schedule, rejects invalid time input, toggles individual apps, signs in through the PIN dialog, and checks locking when closed to the tray using isolated in-memory settings.

## Managed standard accounts

The **Managed account** tab provides administrator controls, separate from personal focus sessions. Enter a local standard-account username, choose restrictions, then select **Review and apply as administrator**. Windows requests administrator credentials. A separate helper inventories the account and displays the exact planned scope, preserved package identities, blocked uninstaller paths, and unresolved uninstall commands before offering Apply. Cancelling that preview leaves Windows policy unchanged.

**Restrict new apps and installer packages** limits executable launch to Windows and Program Files (excluding Windows Temp), denies script files and Windows Installer packages, and restricts packaged apps to the publisher/name identities already installed for the account. This affects portable apps and existing per-user desktop apps too. The Appx collection includes explicit allow rules so executable enforcement does not accidentally disable packaged apps for unrelated accounts.

**Block registered uninstaller programs** adds deny rules for resolved uninstall executable paths from machine and target-user uninstall registries. It requires the target user's registry hive to be loaded: sign into that account, then switch back to the administrator account. Paths with ambiguous commands are reported rather than guessed. An app sharing its executable with its uninstaller can lose normal functionality too. Both options block Windows Installer packages and its launcher, so MSI install, repair, and removal are coupled rather than independent switches.

**Limits:** These are AppLocker restrictions on common launch/package paths, not universal installation/removal prevention. Users can still delete files they own; packaged-app removal, copied/unregistered uninstallers, built-in interpreters, and programs in writable subfolders of allowed directories can offer other routes. Existing package identities remain permitted for updates/reinstallation. Standard-account privileges already protect many machine-wide apps. Administrator credentials bypass the intended account separation. Do not use this helper on MDM-managed PCs.

The helper refuses administrator/current/disabled targets, domain-joined devices, existing SRP or detected MDM AppLocker configuration, pre-existing unmanaged AppLocker rules, and policies changed outside FocusFence. One account is managed at a time. New rules deny only the selected SID and preserve unrestricted access for others. Application Identity is started and set to automatic so policy continues after FocusFence exits.

To disable the restrictions, enter the same username and select **Restore previous policy**. Backups are kept under `%PROGRAMDATA%/FocusFence-AdminPolicy/state.json` with administrator/SYSTEM-only permissions. The helper compares the current policy with its recorded version before restoring; it will not overwrite outside edits. The original Application Identity startup mode is restored; Windows may keep the protected service running until restart. Backup files remain for recovery. If the target account was deleted, or policy was changed externally, use an administrator's manual policy review instead.

After the helper closes, FocusFence shows the account, time, and explicit outcome: applied, cancelled, restored, no policy to restore, or restored with Application Identity still running. Failure and unknown exit codes never count as a successful apply. This reports the completed operation, not continuous enforcement monitoring. Cancelling keeps the saved draft. Account/options controls are disabled while the helper runs so the displayed operation retains its original target.

The helper uses process-local PowerShell execution-policy bypass only to load its bundled scripts; it does not alter machine execution policy. Keep the app/helper files in an administrator-controlled folder. Policy tests build fixtures and call Windows' read-only `Test-AppLockerPolicy`; they never apply the fixtures. Actual elevated apply/restore and sign-in enforcement still require a dedicated standard-account acceptance test. The legacy lockdown/undo scripts are not used.

References: [AppLocker rules and packaged-app requirements](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/working-with-applocker-rules), [policy replacement behavior](https://learn.microsoft.com/en-us/powershell/module/applocker/set-applockerpolicy), [Application Identity service](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/configure-the-application-identity-service).

## Limits and next steps

This is a personal productivity tool, not a security boundary. It checks roughly once per second; apps may briefly appear. Termination can lose unsaved data. Matching is by full path, so moved or updated executables may need reselecting. Elevated apps may deny inspection or termination; failures are shown in the status area. Only processes in the current Windows session are considered. Windows-folder executables, redirected paths, and FocusFence itself are protected. The conservative Windows-folder rule also excludes some harmless bundled apps.

The app runs at sign-in only if you enable the startup preference, and cannot block while closed. Exiting is allowed; saved active sessions can resume later. Changing the system clock affects UTC deadlines. Settings are editable by the local user. Stronger enforcement is a future enhancement.

The existing `lockdown.ps1` and `undo-lockdown.ps1` files are unrelated legacy scripts. FocusFence neither runs nor changes them.
