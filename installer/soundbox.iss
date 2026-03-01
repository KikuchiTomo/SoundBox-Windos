; SoundBox Inno Setup Script
; Requires Inno Setup 6+

#ifndef AppVersion
  #define AppVersion "0.0.1-beta"
#endif

[Setup]
AppId={{B8F3A1D2-7C4E-4F9A-8E6B-1D2C3F4A5B6E}
AppName=SoundBox
AppVersion={#AppVersion}
AppVerName=SoundBox {#AppVersion}
AppPublisher=KikuchiTomo
AppPublisherURL=https://github.com/KikuchiTomo/SoundBox-Windos
AppSupportURL=https://github.com/KikuchiTomo/SoundBox-Windos/issues
DefaultDirName={autopf}\SoundBox
DefaultGroupName=SoundBox
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir=..\installer-output
OutputBaseFilename=SoundBox-{#AppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=compiler:SetupClassicIcon.ico
UninstallDisplayIcon={app}\SoundBox.exe
PrivilegesRequired=admin
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All published files from dotnet publish output
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; License and acknowledgments
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\ACKNOWLEDGMENTS"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\SoundBox"; Filename: "{app}\SoundBox.exe"
Name: "{group}\{cm:UninstallProgram,SoundBox}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SoundBox"; Filename: "{app}\SoundBox.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SoundBox.exe"; Description: "{cm:LaunchProgram,SoundBox}"; Flags: nowait postinstall skipifsilent
