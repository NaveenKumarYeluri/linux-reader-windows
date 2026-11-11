; Inno Setup Script for Linux File Reader for Windows
#define MyAppName "Linux File Reader for Windows"
#define MyAppVersion "1.2.0"
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
SetupIconFile=setup.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\artifacts\gui-win-x64\LinuxReader.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\artifacts\cli-win-x64\linux-reader-cli.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "setup.ico"; DestDir: "{app}"; DestName: "app.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"
Name: "{group}\Linux Reader CLI"; Filename: "{app}\linux-reader-cli.exe"; IconFilename: "{app}\app.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
