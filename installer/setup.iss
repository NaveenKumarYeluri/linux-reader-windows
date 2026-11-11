; Inno Setup Script for Linux File Reader for Windows
#define MyAppName "Linux File Reader for Windows"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Naveen Kumar Yeluri & Antigravity"
#define MyAppURL "https://github.com/NaveenKumarYeluri/linux-reader-windows"
#define MyAppExeName "LinuxReader.exe"

[Setup]
AppId={{5D8A3A81-3E2A-4C2E-8E82-9D1D4A526543}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\Linux File Reader
DefaultGroupName=Linux File Reader
AllowNoIcons=yes
OutputDir=..\dist
OutputBaseFilename=LinuxReader-Setup-x64
SetupIconFile=..\src\LinuxReader.Gui\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\gui-win-x64\LinuxReader.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\cli-win-x64\linux-reader-cli.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\LinuxReader.Gui\Assets\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Linux Reader CLI"; Filename: "{app}\linux-reader-cli.exe"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
