; Inno Setup script for C3850 GUI
; Build with: build.ps1 (runs dotnet publish, then ISCC on this file)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

[Setup]
AppId={{7E2C6F4A-3B1D-4C9E-9A2F-C3850C3850AA}
AppName=C3850 GUI
AppVersion={#AppVersion}
AppVerName=C3850 GUI {#AppVersion}
AppPublisher=SirBiggin
AppPublisherURL=https://github.com/SirBiggin/C3850-GUI
AppSupportURL=https://github.com/SirBiggin/C3850-GUI/issues
DefaultDirName={autopf}\C3850 GUI
DefaultGroupName=C3850 GUI
UninstallDisplayIcon={app}\C3850GUI.exe
OutputDir=..\dist
OutputBaseFilename=C3850-GUI-Setup-{#AppVersion}
SetupIconFile=..\src\C3850GUI\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
MinVersion=10.0.17763
DisableProgramGroupPage=yes
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\C3850 GUI"; Filename: "{app}\C3850GUI.exe"
Name: "{group}\Uninstall C3850 GUI"; Filename: "{uninstallexe}"
Name: "{autodesktop}\C3850 GUI"; Filename: "{app}\C3850GUI.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\C3850GUI.exe"; Description: "{cm:LaunchProgram,C3850 GUI}"; Flags: nowait postinstall skipifsilent
