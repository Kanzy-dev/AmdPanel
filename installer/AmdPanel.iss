; Inno Setup installer script for AmdPanel
; Supports Russian and English languages

#define MyAppName "AmdPanel"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "AmdPanel contributors"
#define MyAppURL "https://github.com/kanzy/amdpanel"
#define MyAppExeName "AmdPanel.exe"

[Setup]
AppId={{A8B4C3D2-E1F0-4567-89AB-CDEF01234567}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={pf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=.
OutputBaseFilename=AmdPanel-Setup-x64
SetupIconFile=..\src\AmdPanel.App\Assets\app.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
LicenseFile=..\LICENSE

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "launchapp"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: unchecked
Name: "autostart"; Description: "Start AmdPanel when Windows starts"; Flags: unchecked

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Include native bridge DLL if it exists
Source: "..\build\NativeBridge\AmdPanel.NativeBridge.dll"; DestDir: "{app}\lib"; Flags: ignoreversion
Source: "..\build\NativeBridge\AmdPanel.NativeBridge.pdb"; DestDir: "{app}\lib"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commonappdata}\Microsoft\Windows\Start Menu\Programs\Startup\AmdPanel"; Filename: "{app}\{#MyAppExeName}"; Parameters: "/minimize"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Tasks: launchapp

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}"; ValueType: string; ValueName: "DisplayName"; ValueData: "{#MyAppName}"

; Clean up context menu keys on uninstall
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\AmdPanel"; Flags: uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\AmdPanel\command"; Flags: uninsdeletekey

[Code]
function InitializeSetup(): Boolean;
begin
  // Initialize setup without build-time checks (these are only relevant for ISCC compilation)
  // The files will be packaged during installation from the installer's built-in sources
  Result := True;
end;

[Messages]
WelcomeLabel2=This installer will guide you through the installation of AmdPanel. [ApplicationName] provides a lightweight native control panel for AMD GPUs.
SetupTitle=AmdPanel Setup Wizard
SetupAppname=AmdPanel
SelectDirBrowseLabel=Select the directory to install [name] in:
SelectDirText=Select destination directory
SelectDirBrowseButton=Browse...
SelectDirCreateLabel=Create folder:
SelectDirCreateButton=Create
SelectDirNotExistLabel=The directory you entered does not exist. Create it?
SelectDirExistsLabel=The directory you entered already exists. Continue?
SelectDirYesToAll=Yes to all
SelectDirNoToAll=No to all
SelectDirNo=No
SelectDirYes=Yes
BeveledLabel=Setup

[UninstallDelete]
; Clean up start menu shortcut if it exists
Type: filesandordirs; Name: "{commonappdata}\Microsoft\Windows\Start Menu\Programs\Startup\AmdPanel.lnk"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssDone then
  begin
    // Startup shortcut is already created in [Icons] section when autostart task is selected
  end;
end;