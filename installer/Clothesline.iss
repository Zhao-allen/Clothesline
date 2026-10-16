; Clothesline installer script (Inno Setup 6)
; Compile: ISCC.exe /DAppVersion=0.1.9 Clothesline.iss
; Per-user install (no admin required) to %LOCALAPPDATA%\Programs\Clothesline.

#ifndef AppVersion
  #define AppVersion "0.1.9"
#endif

[Setup]
AppName=Clothesline
AppVersion={#AppVersion}
AppPublisher=Clothesline
AppPublisherURL=https://github.com/Zhao-allen/Clothesline
AppSupportURL=https://github.com/Zhao-allen/Clothesline/issues
AppUpdatesURL=https://github.com/Zhao-allen/Clothesline/releases
DefaultDirName={localappdata}\Programs\Clothesline
DefaultGroupName=Clothesline
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\publish
OutputBaseFilename=Clothesline-Setup-{#AppVersion}
SetupIconFile=..\Resources\Clothesline.ico
UninstallDisplayIcon={app}\Clothesline.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\Clothesline.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Resources\Clothesline.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Clothesline"; Filename: "{app}\Clothesline.exe"; IconFilename: "{app}\Clothesline.ico"
Name: "{autodesktop}\Clothesline"; Filename: "{app}\Clothesline.exe"; IconFilename: "{app}\Clothesline.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\Clothesline.exe"; Description: "{cm:LaunchProgram,Clothesline}"; Flags: nowait postinstall skipifsilent
