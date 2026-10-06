; 佳怡桌宠精简安装包。语音走公网转发，不打包本机语音运行时。
; 密钥文件 config\llm.runtime.json 不进入安装包。

#define MyAppName "佳怡桌宠（共用语音）"
#define MyAppVersion "1.8.0"
#define MyAppExeName "CompanionDesktopPet.exe"
#define RepoRoot "D:\desktop\CompanionDesktopPet"

[Setup]
AppId={{C4E91B72-6A38-4D05-8F17-3B6D2E90A1C8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=haohaizi554
DefaultDirName={autopf}\CompanionDesktopPet-SharedVoice
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#RepoRoot}\outputs\installer-shared-voice
OutputBaseFilename=Jiayi-Desktop-Pet-Setup-SharedVoice
SetupIconFile={#RepoRoot}\src\CompanionDesktopPet\Assets\pet.ico
Compression=lzma2
SolidCompression=yes
LZMAUseSeparateProcess=yes
LZMANumBlockThreads=2
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
InfoBeforeFile={#RepoRoot}\packaging\install-notes-shared-voice-zh.txt
CloseApplications=yes
VersionInfoVersion=1.8.0.0
VersionInfoProductVersion=1.8.0

[Languages]
Name: "chinesesimp"; MessagesFile: "{#RepoRoot}\packaging\ChineseSimplified.islu"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加图标:"; Flags: checkedonce

[Files]
Source: "{#RepoRoot}\outputs\standalone-shared-voice\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
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
