; Inno Setup script for The Airline Project (Windows, per-user install).
; Build with Installer\Inno\build-installer.ps1, or let the GitHub workflow do it.
#ifndef AppVersion
  #define AppVersion "0.4.40"
#endif
#ifndef PublishDir
  #define PublishDir "..\..\artifacts\publish"
#endif

[Setup]
AppId={{6F0E4B6A-7B63-4C0C-9E54-0A1B7B2D3C41}
AppName=The Airline Project
AppVersion={#AppVersion}
AppPublisher=The Airline Project
DefaultDirName={autopf}\The Airline Project
DefaultGroupName=The Airline Project
UninstallDisplayIcon={app}\TheAirline.exe
SetupIconFile=..\..\TheAirline.ico
LicenseFile=..\Resources\License.rtf
OutputDir=..\..\artifacts
OutputBaseFilename=TheAirlineProject-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; The game writes custom airlines and logos into its data folder, so it installs per user (no admin rights needed).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\The Airline Project"; Filename: "{app}\TheAirline.exe"
Name: "{group}\Uninstall The Airline Project"; Filename: "{uninstallexe}"
Name: "{autodesktop}\The Airline Project"; Filename: "{app}\TheAirline.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TheAirline.exe"; Description: "{cm:LaunchProgram,The Airline Project}"; Flags: nowait postinstall skipifsilent

; Saved games and settings live in Documents\theairlineproject, so uninstalling never touches them.
