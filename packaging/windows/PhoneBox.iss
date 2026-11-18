[Setup]
AppId=net.oxog.phonebox.agent
AppName=PhoneBox
AppVersion={#Version}
AppPublisher=PhoneBox
DefaultDirName={localappdata}\PhoneBox\app
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os or arm64
ArchitecturesInstallIn64BitMode=x64os or arm64
MinVersion=10.0.19041
UninstallDisplayIcon={app}\PhoneBoxAgent.exe
SetupIconFile=..\..\agent\PhoneBox.Agent\phonebox.ico
OutputDir=..\..\dist
OutputBaseFilename=PhoneBox-{#Version}-windows-universal
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\..\payload\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs; Check: not IsArm64
Source: "..\..\payload\win-arm64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs; Check: IsArm64

[Icons]
Name: "{userprograms}\PhoneBox"; Filename: "{app}\PhoneBoxAgent.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\PhoneBoxAgent.exe"; Description: "Start PhoneBox"; Flags: nowait postinstall skipifsilent
