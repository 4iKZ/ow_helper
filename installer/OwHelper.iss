; OW 助手安装脚本（Inno Setup 6）
;
; 编译（仓库根目录执行）：
;   ISCC.exe installer/OwHelper.iss /DAppVersion=1.0.1 /DSourceDir=artifacts/OwHelper-win-x64
; 不传参数时使用默认值：AppVersion=0.0.0（CI 冒烟），SourceDir=自包含发布目录。
; ISCC 的当前目录可以是任意位置：脚本内路径全部基于 SourcePath（脚本所在目录）。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir SourcePath + "\\..\\artifacts\\OwHelper-win-x64"
#endif
#define MyAppName "OW 助手"
#define MyAppExe "OwHelper.Desktop.exe"

[Setup]
AppId={{C7E3A54E-7FE6-4A9A-A1C2-00115B0D0892}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppVerName={#MyAppName} {#AppVersion}
AppPublisher=SiyuanHao
DefaultDirName={localappdata}\Programs\OW Helper
DefaultGroupName=OW 助手
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#SourcePath}\..\artifacts\packages
OutputBaseFilename=OwHelper-Setup-{#AppVersion}
SetupIconFile={#SourcePath}\owhelper.ico
UninstallDisplayIcon={app}\{#MyAppExe}
LicenseFile={#SourcePath}\..\LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoDescription=OW 助手安装程序

[Languages]
; 中文语言文件 vendoring 进仓库：CI 预装的 Inno 精简包不带官方中文翻译。
Name: "chinesesimplified"; MessagesFile: "compiler:Default.isl,{#SourcePath}\Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; 只发桌面版（含共享依赖）；控制台与诊断工具留在 zip 包里给折腾党。
; 调试符号不进安装包。
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "*.pdb,OwHelper.exe,BgKeyProbe.exe"

[Icons]
Name: "{group}\OW 助手"; Filename: "{app}\{#MyAppExe}"
Name: "{group}\卸载 OW 助手"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OW 助手"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "运行 OW 助手"; Flags: nowait postinstall skipifsilent
