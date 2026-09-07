#ifndef AppVersion
  #error AppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by Build-Installer.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be supplied by Build-Installer.ps1
#endif

[Setup]
AppId={{24946476-09F2-4540-917B-65D5D3CD2F51}
AppName=FocusFence
AppVersion={#AppVersion}
AppPublisher=FocusFence
AppPublisherURL=https://github.com/tannpv/FocusFence
AppSupportURL=https://github.com/tannpv/FocusFence/issues
DefaultDirName={localappdata}\Programs\FocusFence
DefaultGroupName=FocusFence
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=FocusFence-{#AppVersion}-Setup-x64
SetupIconFile=..\src\FocusFence.App\Assets\FocusFence.ico
UninstallDisplayIcon={app}\FocusFence.App.exe
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
AppMutex=Local\FocusFence.Desktop
CloseApplications=no
RestartApplications=no
Uninstallable=yes
VersionInfoVersion={#AppVersion}

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\src\FocusFence.App\Assets\FocusFence.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\FocusFence"; Filename: "{app}\FocusFence.App.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\FocusFence"; Filename: "{app}\FocusFence.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FocusFence.App.exe"; Description: "Launch FocusFence"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := not CheckForMutexes('Local\FocusFence.Desktop');
  if not Result then
    MsgBox('Exit FocusFence from its system tray menu before uninstalling.', mbInformation, MB_OK);
end;
