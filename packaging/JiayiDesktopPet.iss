; 佳怡桌宠安装包。语音运行时和对话程序与主程序装在同一目录。
; 密钥文件 config\llm.runtime.json 不进入安装包。

#define MyAppName "佳怡桌宠"
#define MyAppVersion "1.6.0"
#define MyAppExeName "CompanionDesktopPet.exe"
#define RepoRoot "D:\desktop\CompanionDesktopPet"
#define SliceBytes "1800000000"

[Setup]
AppId={{8F3A6C21-5B74-4E19-9C0D-2A7E4B91D6F0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=haohaizi554
DefaultDirName={autopf}\CompanionDesktopPet
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#RepoRoot}\outputs\installer
OutputBaseFilename=Jiayi-Desktop-Pet-Setup
SetupIconFile={#RepoRoot}\src\CompanionDesktopPet\Assets\pet.ico
Compression=lzma2
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=2
DiskSpanning=yes
DiskSliceSize={#SliceBytes}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoBeforeFile={#RepoRoot}\packaging\install-notes-zh.txt
CloseApplications=yes
VersionInfoVersion=1.6.0.0
VersionInfoProductVersion=1.6.0

[Languages]
Name: "chinesesimp"; MessagesFile: "{#RepoRoot}\packaging\ChineseSimplified.islu"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: checkedonce

[Files]
Source: "{#RepoRoot}\outputs\standalone\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\outputs\standalone\voice\*"; DestDir: "{app}\voice"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*\__pycache__\*,*.pyc"
Source: "{#RepoRoot}\dialogue\serve.py"; DestDir: "{app}\dialogue"; Flags: ignoreversion
Source: "{#RepoRoot}\dialogue\persona_dialogue\*"; DestDir: "{app}\dialogue\persona_dialogue"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*\__pycache__\*,*.pyc"
Source: "{#RepoRoot}\dialogue\python\*"; DestDir: "{app}\dialogue\python"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*\__pycache__\*,*.pyc"
Source: "{#RepoRoot}\data\persona\jiayi-soul.json"; DestDir: "{app}\data\persona"; Flags: ignoreversion
Source: "{#RepoRoot}\data\optimized\persona-corpus-v2.tsv"; DestDir: "{app}\data\optimized"; Flags: ignoreversion
Source: "{#RepoRoot}\data\optimized\line-selection-model.json"; DestDir: "{app}\data\optimized"; Flags: ignoreversion
Source: "{#RepoRoot}\config\llm.runtime.example.json"; DestDir: "{app}\config"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE.md"; DestDir: "{app}"; DestName: "LICENSE.md"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE-SCOPE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\ASSET_AND_PERSONA_RIGHTS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\NOTICE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动{#MyAppName}"; Flags: nowait postinstall skipifsilent
