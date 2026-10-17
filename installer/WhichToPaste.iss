; WhichToPaste 安装包脚本（Inno Setup 6.x）
;
; 设计要点：
;   - 按用户安装到 %LOCALAPPDATA%\Programs\WhichToPaste，不需要管理员权限；
;   - 主程序是常驻托盘的小工具，安装/卸载前先结束正在运行的实例；
;   - 卸载时移除程序自己写的开机自启注册表项，并询问是否删除历史记录与设置；
;   - 版本号由 build-installer.ps1 通过 /DAppVersion=... 传入，默认 1.0.0。

#define AppGuid "{{8C3B7F2A-5D41-4E9B-9A6C-2F7B1E4D8A10}"
#define AppName "WhichToPaste"
#define AppPublisher "Sparrived"
#define AppUrl "https://github.com/Sparrived/WhichToPaste"
#define AppExe "WhichToPaste.exe"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={#AppGuid}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppComments=复制即记录，粘贴时先挑一条再粘。
DefaultDirName={localappdata}\Programs\WhichToPaste
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
MinVersion=10.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=assets\wizard.bmp
WizardSmallImageFile=assets\wizard-small.bmp
OutputDir=..\artifacts
OutputBaseFilename=WhichToPaste-Setup-{#AppVersion}-x64
SetupIconFile=assets\WhichToPaste.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
ShowLanguageDialog=no

[Languages]
Name: "chs"; MessagesFile: "lang\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 自包含发布产物：目标机不需要预装 .NET 运行时
Source: "..\artifacts\payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "剪贴板历史选择器"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "剪贴板历史选择器"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 程序运行后可能在安装目录留下文件，卸载时一并清掉
Type: filesandordirs; Name: "{app}"

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = 'WhichToPaste';
  SingleInstanceMutex = 'Local\WhichToPaste.SingleInstance';

{ 托盘程序结束时不弹窗，靠单实例互斥体判断是否在运行 }
procedure StopRunningInstance;
var
  ResultCode: Integer;
begin
  if not CheckForMutexes(SingleInstanceMutex) then
    Exit;

  Log('检测到 WhichToPaste 正在运行，结束进程以便替换文件');
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM WhichToPaste.exe /F /T', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
  Sleep(800);
end;

{ 程序自己写的开机自启项：卸载后指向不存在的路径，必须清掉 }
procedure RemoveAutoStartValue;
begin
  if RegValueExists(HKEY_CURRENT_USER, RunKey, RunValueName) then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, RunValueName);
    Log('已移除开机自启注册表项');
  end;
end;

function InitializeSetup: Boolean;
begin
  StopRunningInstance;
  Result := True;
end;

{ 卸载器命令行走 ParamStr 遍历（Inno 6.0 没有 CmdLineParamExists） }
function WantsDeleteUserData: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
  begin
    if CompareText(ParamStr(I), '/DELETEUSERDATA') = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ 快捷方式通常由 Inno 自动删除；个别时候会被安全软件或资源管理器短暂占用，
  卸载结束前再补删一次，避免留下指向已删除程序的死链接 }
procedure CleanupShortcut(Shortcut: String);
var
  Attempt: Integer;
begin
  for Attempt := 1 to 3 do
  begin
    if not FileExists(Shortcut) then
      Exit;
    DeleteFile(Shortcut);
    Sleep(150);
  end;
  if FileExists(Shortcut) then
    Log('快捷方式未能删除：' + Shortcut);
end;

procedure CleanupLeftoverShortcuts;
begin
  CleanupShortcut(ExpandConstant('{autoprograms}\{#AppName}.lnk'));
  CleanupShortcut(ExpandConstant('{autodesktop}\{#AppName}.lnk'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  case CurUninstallStep of
    usUninstall:
      begin
        StopRunningInstance;
        RemoveAutoStartValue;
      end;

    usPostUninstall:
      begin
        CleanupLeftoverShortcuts;
        DataDir := ExpandConstant('{userappdata}\WhichToPaste');
        if DirExists(DataDir) then
        begin
          { 默认按钮是“否”：静默卸载（/SUPPRESSMSGBOXES）时会保留数据；
            /DELETEUSERDATA 供脚本化卸载直接删除，不再询问 }
          if WantsDeleteUserData or
             (MsgBox('是否同时删除历史记录与设置？' + #13#10 + #13#10 +
                     '数据目录：' + DataDir + #13#10 +
                     '选择“否”将保留这些数据，方便以后重装。',
                     mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
          begin
            DelTree(DataDir, True, True, True);
            Log('已删除数据目录：' + DataDir);
          end;
        end;
      end;
  end;
end;
