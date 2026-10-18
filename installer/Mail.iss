; Setup for Mail – built by deploy.ps1:
;   ISCC.exe /DAppVersion=1.2.3 /DSourceDir=<publish folder> /DOutputDir=<artifacts> installer\Mail.iss
; Installs per user (no admin) into a writable folder so the in-app updater can replace files.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6B2E4F1A-9C3D-4E8B-A7F2-0D1C5E9B3A47}
AppName=Mail
AppVersion={#AppVersion}
AppVerName=Mail {#AppVersion}
AppPublisher=Kuba
AppPublisherURL=https://github.com/Kuba0734/0x-Mailclient
AppUpdatesURL=https://github.com/Kuba0734/0x-Mailclient/releases
DefaultDirName={localappdata}\Programs\Mail
DefaultGroupName=Mail
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=MailSetup-x64
SetupIconFile=..\MailClient.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\MailClient.App.exe
UninstallDisplayName=Mail
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Mail"; Filename: "{app}\MailClient.App.exe"
Name: "{autodesktop}\Mail"; Filename: "{app}\MailClient.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\MailClient.App.exe"; Description: "{cm:LaunchProgram,Mail}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; End a running (tray) instance before files are removed.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM MailClient.App.exe"; Flags: runhidden; RunOnceId: "KillMail"

[Registry]
; Remove the "start with Windows" entry the app may have created (see AutostartService).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MailClient"; Flags: uninsdeletevalue dontcreatekey

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
