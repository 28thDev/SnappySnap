#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be supplied by build-installer.ps1
#endif
#ifndef InstallerOutput
  #error InstallerOutput must be supplied by build-installer.ps1
#endif
#define ProductId "{DC69B5B3-5B91-4678-BDA3-C0F0F6AB5102}"

[Setup]
AppId={{DC69B5B3-5B91-4678-BDA3-C0F0F6AB5102}
AppName=SnappySnap
AppVersion={#AppVersion}
AppVerName=SnappySnap {#AppVersion}
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\SnappySnap
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
SetupArchitecture=x64
MinVersion=10.0.22000
AppMutex={code:RunningMutex}
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
AllowCancelDuringInstall=no
UninstallDisplayIcon={app}\SnappySnap.exe
SetupIconFile=assets\SnappySnap.ico
WizardStyle=modern dark includetitlebar
WizardImageFile=assets\wizard.png
WizardSmallImageFile=assets\mark.png
DefaultDialogFontName=Segoe UI
ShowLanguageDialog=no
LanguageDetectionMethod=none
UsePreviousLanguage=no
OutputDir={#InstallerOutput}
OutputBaseFilename=SnappySnap-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Messages]
english.WelcomeLabel2=This will install SnappySnap for the current user. Administrator permission and an internet connection are not required.%n%nCaptures and settings stay separate from the application.
russian.WelcomeLabel2=Установка SnappySnap для текущего пользователя. Права администратора и подключение к интернету не требуются.%n%nЗахваты и настройки хранятся отдельно от программы.
chinese.WelcomeLabel2=此安装程序将为当前用户安装 SnappySnap。无需管理员权限。%n%n捕获内容和设置与程序本身分开保存。
japanese.WelcomeLabel2=現在のユーザー向けに SnappySnap をインストールします。管理者権限は必要ありません。%n%nキャプチャと設定はアプリとは別に保存されます。
spanish.WelcomeLabel2=Esto instalará SnappySnap para el usuario actual. No se necesitan permisos de administrador.%n%nLas capturas y la configuración se mantienen separadas de la aplicación.
english.FinishedLabel=SnappySnap is installed. After launch, look for its icon in the system tray near the clock. Click it to open your capture history.
russian.FinishedLabel=SnappySnap установлен. После запуска найдите значок в системном трее рядом с часами. Щелчок открывает историю захватов.
chinese.FinishedLabel=SnappySnap 已安装。启动后，请在时钟旁的系统托盘中查找图标。点击图标可打开捕获历史。
japanese.FinishedLabel=SnappySnap をインストールしました。起動後、時計の近くのシステムトレイにあるアイコンをクリックするとキャプチャ履歴を開けます。
spanish.FinishedLabel=SnappySnap está instalado. Después de iniciarlo, busca su icono en la bandeja del sistema junto al reloj. Haz clic para abrir el historial de capturas.
english.ConfirmUninstall=Remove SnappySnap?%n%nYour captures, settings, history and recovery files will be kept.
russian.ConfirmUninstall=Удалить SnappySnap?%n%nЗахваты, настройки, история и файлы восстановления будут сохранены.
chinese.ConfirmUninstall=要删除 SnappySnap 吗？%n%n捕获内容、设置、历史记录和恢复文件将保留。
japanese.ConfirmUninstall=SnappySnap を削除しますか？%n%nキャプチャ、設定、履歴、復元ファイルは保持されます。
spanish.ConfirmUninstall=¿Quieres quitar SnappySnap?%n%nTus capturas, configuración, historial y archivos de recuperación se conservarán.

[CustomMessages]
english.Startup=Start SnappySnap when I sign in to Windows
russian.Startup=Запускать SnappySnap при входе в Windows
english.Desktop=Create a desktop shortcut
russian.Desktop=Создать ярлык на рабочем столе
english.Launch=Launch SnappySnap in the system tray
russian.Launch=Запустить SnappySnap в системном трее
chinese.Startup=登录 Windows 时启动 SnappySnap
chinese.Desktop=创建桌面快捷方式
chinese.Launch=在系统托盘中启动 SnappySnap
japanese.Startup=Windows へのサインイン時に SnappySnap を起動
japanese.Desktop=デスクトップショートカットを作成
japanese.Launch=システムトレイで SnappySnap を起動
spanish.Startup=Iniciar SnappySnap al iniciar sesión en Windows
spanish.Desktop=Crear un acceso directo en el escritorio
spanish.Launch=Iniciar SnappySnap en la bandeja del sistema
english.Maintenance=Another SnappySnap installation or removal is in progress. Please wait for it to finish.
russian.Maintenance=Установка или удаление SnappySnap уже выполняется. Дождитесь завершения.
chinese.Maintenance=正在安装或删除另一个 SnappySnap。请等待其完成。
japanese.Maintenance=別の SnappySnap のインストールまたは削除が進行中です。完了するまでお待ちください。
spanish.Maintenance=Hay otra instalación o eliminación de SnappySnap en curso. Espera a que termine.
english.Running=SnappySnap is running. Finish your capture or export, save your edits, then choose Exit from its tray menu and run Setup again.
russian.Running=SnappySnap запущен. Завершите захват или экспорт, сохраните изменения и выберите Exit в меню значка в трее. Затем повторите установку.
chinese.Running=SnappySnap 正在运行。请完成捕获或导出、保存编辑内容，然后从托盘菜单选择退出，再次运行安装程序。
japanese.Running=SnappySnap は実行中です。キャプチャまたはエクスポートを完了して編集内容を保存し、トレイメニューから終了を選んでから Setup を再実行してください。
spanish.Running=SnappySnap está en ejecución. Termina la captura o exportación, guarda los cambios, elige Salir en el menú de la bandeja y vuelve a ejecutar Setup.
english.Downgrade=A newer version of SnappySnap is installed. Installing an older version is not supported. Your existing installation has not been changed.
russian.Downgrade=Установлена более новая версия SnappySnap. Установка старой версии не поддерживается. Текущая установка не изменена.
chinese.Downgrade=已安装较新的 SnappySnap 版本。不支持安装旧版本。现有安装未更改。
japanese.Downgrade=新しいバージョンの SnappySnap がインストールされています。古いバージョンへの変更はサポートされません。既存のインストールは変更されていません。
spanish.Downgrade=Hay instalada una versión más reciente de SnappySnap. No se admite instalar una versión anterior. La instalación existente no se ha modificado.
english.VersionError=The installed version could not be read. Setup has stopped without replacing the application.
russian.VersionError=Не удалось определить установленную версию. Установка остановлена без замены приложения.
chinese.VersionError=无法读取已安装的版本。安装程序已停止，应用未被替换。
japanese.VersionError=インストールされているバージョンを読み取れませんでした。アプリを置き換えずに Setup を停止しました。
spanish.VersionError=No se pudo leer la versión instalada. Setup se detuvo sin reemplazar la aplicación.
english.ConfigureError=Could not apply the Windows startup setting. Installation did not complete successfully. See the setup log and SnappySnap logs in LocalAppData\SnappySnap\Logs, then run Setup again.
russian.ConfigureError=Не удалось применить настройку автозапуска. Установка не завершена успешно. Проверьте журнал установки и журналы в LocalAppData\SnappySnap\Logs, затем повторите установку.
chinese.ConfigureError=无法应用 Windows 启动设置。安装未成功完成。请查看安装日志和 LocalAppData\SnappySnap\Logs 中的 SnappySnap 日志，然后再次运行安装程序。
japanese.ConfigureError=Windows のスタートアップ設定を適用できませんでした。インストールは正常に完了していません。Setup ログと LocalAppData\SnappySnap\Logs の SnappySnap ログを確認してから、Setup を再実行してください。
spanish.ConfigureError=No se pudo aplicar la configuración de inicio de Windows. La instalación no se completó correctamente. Consulta el registro de Setup y los registros de SnappySnap en LocalAppData\SnappySnap\Logs y vuelve a ejecutar Setup.
english.Incomplete=SnappySnap setup needs attention
russian.Incomplete=Установка SnappySnap требует внимания
chinese.Incomplete=SnappySnap 安装需要处理
japanese.Incomplete=SnappySnap のセットアップに対応が必要です
spanish.Incomplete=La instalación de SnappySnap necesita atención
english.KeepStartup=Your existing startup preference will be preserved. You can change it in SnappySnap Settings.
russian.KeepStartup=Текущая настройка автозапуска будет сохранена. Изменить её можно в настройках SnappySnap.
chinese.KeepStartup=将保留现有的启动设置。你可以在 SnappySnap 设置中更改它。
japanese.KeepStartup=既存のスタートアップ設定は保持されます。SnappySnap の設定で変更できます。
spanish.KeepStartup=Se conservará la preferencia de inicio existente. Puedes cambiarla en la configuración de SnappySnap.
english.MediaFoundation=Open Windows Optional Features for Media Feature Pack (Windows N only)
russian.MediaFoundation=Открыть дополнительные компоненты Windows для Media Feature Pack (только Windows N)
chinese.MediaFoundation=打开 Windows 可选功能以添加 Media Feature Pack（仅限 Windows N）
japanese.MediaFoundation=Windows のオプション機能で Media Feature Pack を開く（Windows N のみ）
spanish.MediaFoundation=Abrir las características opcionales de Windows para Media Feature Pack (solo Windows N)
english.MediaFoundationNotice=Windows Media Foundation was not detected. Video recording on Windows N may need Media Feature Pack. Select the optional task below to open Windows Optional Features after setup.
russian.MediaFoundationNotice=Windows Media Foundation не обнаружен. Для записи видео в Windows N может понадобиться Media Feature Pack. Выберите необязательную задачу ниже, чтобы после установки открыть дополнительные компоненты Windows.
chinese.MediaFoundationNotice=未检测到 Windows Media Foundation。Windows N 上的视频录制可能需要 Media Feature Pack。选择下面的可选任务，以便安装后打开 Windows 可选功能。
japanese.MediaFoundationNotice=Windows Media Foundation が見つかりません。Windows N で動画を録画するには Media Feature Pack が必要な場合があります。下のオプションを選ぶと、セットアップ後に Windows のオプション機能を開きます。
spanish.MediaFoundationNotice=No se detectó Windows Media Foundation. La grabación de vídeo en Windows N puede necesitar Media Feature Pack. Selecciona la tarea opcional para abrir las características opcionales de Windows después de la instalación.

[Tasks]
Name: "startup"; Description: "{cm:Startup}"; Check: IsFreshProfile
Name: "desktopicon"; Description: "{cm:Desktop}"; Flags: unchecked
Name: "mediafoundation"; Description: "{cm:MediaFoundation}"; Flags: unchecked; Check: NeedsMediaFoundation

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\SnappySnap"; Filename: "{app}\SnappySnap.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\SnappySnap"; Filename: "{app}\SnappySnap.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "ms-settings:optionalfeatures"; Description: "{cm:MediaFoundation}"; Flags: postinstall shellexec skipifsilent; Check: WizardIsTaskSelected('mediafoundation')
Filename: "{app}\SnappySnap.exe"; Description: "{cm:Launch}"; Flags: nowait postinstall skipifsilent; Check: CanLaunch; BeforeInstall: ReleaseMaintenance

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductId}_is1';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
type
  TTokenUser = record
    Sid: NativeUInt;
    AttributesAndPadding: NativeUInt;
    SidBuffer: array[0..255] of Byte;
  end;
function GetCurrentProcess(): THandle;
  external 'GetCurrentProcess@kernel32.dll stdcall';
function OpenProcessToken(Process: THandle; Access: Cardinal; var Token: THandle): Boolean;
  external 'OpenProcessToken@advapi32.dll stdcall';
function GetTokenInformation(Token: THandle; InfoClass: Integer; var User: TTokenUser; Length: Cardinal; var Required: Cardinal): Boolean;
  external 'GetTokenInformation@advapi32.dll stdcall';
function ConvertSidToStringSid(Sid: NativeUInt; var Text: NativeUInt): Boolean;
  external 'ConvertSidToStringSidW@advapi32.dll stdcall';
function CopyString(Dest: String; Source: NativeUInt; Count: Integer): NativeUInt;
  external 'lstrcpynW@kernel32.dll stdcall';
function LocalFree(Address: NativeUInt): NativeUInt;
  external 'LocalFree@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function CreateNamedMutex(Security: NativeUInt; Owner: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';

var
  CachedSid: String;
  MaintenanceHandle: THandle;
  ExistingProfile: Boolean;
  ConfigurationFailed: Boolean;

function UserSid(): String;
var Token: THandle; User: TTokenUser; Required: Cardinal; Address: NativeUInt; Text: String;
begin
  if CachedSid = '' then begin
    if not OpenProcessToken(GetCurrentProcess(), $0008, Token) then RaiseLastException;
    try
      { Buffer includes variable SID data returned after the TOKEN_USER header. }
      if not GetTokenInformation(Token, 1, User, SizeOf(User), Required) then RaiseLastException;
      if not ConvertSidToStringSid(User.Sid, Address) then RaiseLastException;
      try
        SetLength(Text, 256);
        CopyString(Text, Address, 256);
        CachedSid := Copy(Text, 1, Pos(#0, Text) - 1);
      finally LocalFree(Address); end;
    finally CloseHandle(Token); end;
  end;
  Result := CachedSid;
end;

function RunningMutex(Param: String): String;
begin
  Result := 'Global\SnappySnap.Running.' + UserSid();
end;

procedure ReleaseMaintenance();
begin
  if MaintenanceHandle <> 0 then begin
    CloseHandle(MaintenanceHandle);
    MaintenanceHandle := 0;
  end;
end;

function EnterMaintenance(): Boolean;
var AlreadyExists: Boolean;
begin
  Result := False;
  MaintenanceHandle := CreateNamedMutex(0, False, 'Global\SnappySnap.Maintenance.' + UserSid());
  AlreadyExists := DLLGetLastError = 183;
  if MaintenanceHandle = 0 then RaiseLastException;
  if AlreadyExists then begin
    SuppressibleMsgBox(CustomMessage('Maintenance'), mbError, MB_OK, IDOK);
    ReleaseMaintenance(); exit;
  end;
  if CheckForMutexes(RunningMutex('')) then begin
    SuppressibleMsgBox(CustomMessage('Running'), mbError, MB_OK, IDOK);
    ReleaseMaintenance(); exit;
  end;
  Result := True;
end;

function IsFreshProfile(): Boolean;
begin
  Result := not ExistingProfile;
end;

function NeedsMediaFoundation(): Boolean;
begin
  { Windows 11 normally includes these. Windows N may require the optional Media Feature Pack. }
  Result := not FileExists(ExpandConstant('{sys}\mfplat.dll')) or
    not FileExists(ExpandConstant('{sys}\mfreadwrite.dll'));
end;

function InitializeSetup(): Boolean;
var OldText: String; OldVersion, NewVersion: Int64;
begin
  Result := False;
  if not EnterMaintenance() then exit;
  ExistingProfile := FileExists(ExpandConstant('{localappdata}\SnappySnap\settings.json')) or RegKeyExists(HKCU64, UninstallKey);
  if RegQueryStringValue(HKCU64, UninstallKey, 'DisplayVersion', OldText) then begin
    if not StrToVersion(OldText, OldVersion) or not StrToVersion('{#AppVersion}', NewVersion) then begin
      SuppressibleMsgBox(CustomMessage('VersionError'), mbError, MB_OK, IDOK); exit;
    end;
    if ComparePackedVersion(OldVersion, NewVersion) > 0 then begin
      SuppressibleMsgBox(CustomMessage('Downgrade'), mbError, MB_OK, IDOK); exit;
    end;
  end;
  Result := True;
end;

procedure InitializeWizard();
begin
  { /DIR must not redirect this per-user installation into an arbitrary data directory. }
  WizardForm.DirEdit.Text := ExpandConstant('{localappdata}\Programs\SnappySnap');
  if ExistingProfile then WizardForm.SelectTasksLabel.Caption := CustomMessage('KeepStartup');
  if NeedsMediaFoundation() then
    WizardForm.SelectTasksLabel.Caption := WizardForm.SelectTasksLabel.Caption + #13#10#13#10 + CustomMessage('MediaFoundationNotice');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var Mode: String; ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    Mode := 'preserve';
    if not ExistingProfile then begin
      if WizardIsTaskSelected('startup') then Mode := 'on' else Mode := 'off';
    end;
    { ssPostInstall exceptions are nonfatal in Inno. Track failure explicitly so
      Setup cannot report success or launch the app after a configuration error. }
    ConfigurationFailed := True;
    if Exec(ExpandConstant('{app}\SnappySnap.exe'), '--configure-startup=' + Mode,
      ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ConfigurationFailed := ResultCode <> 0;
    if ConfigurationFailed then begin
      Log(CustomMessage('ConfigureError'));
      Log(Format('Startup configuration exit/error code: %d', [ResultCode]));
    end;
    { Remove only the shortcut owned by this installer when deselected on a later run. }
    if not WizardIsTaskSelected('desktopicon') then DeleteFile(ExpandConstant('{userdesktop}\SnappySnap.lnk'));
  end;
end;

function CanLaunch(): Boolean;
begin
  Result := not ConfigurationFailed;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpFinished) and ConfigurationFailed then begin
    WizardForm.FinishedHeadingLabel.Caption := CustomMessage('Incomplete');
    WizardForm.FinishedLabel.Caption := CustomMessage('ConfigureError');
    WizardForm.RunList.Visible := False;
  end;
end;

function GetCustomSetupExitCode(): Integer;
begin
  if ConfigurationFailed then Result := 31 else Result := 0;
end;

procedure DeinitializeSetup();
begin
  ReleaseMaintenance();
end;

function InitializeUninstall(): Boolean;
begin
  Result := EnterMaintenance();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usUninstall then begin
    if RegQueryStringValue(HKCU, RunKey, 'SnappySnap', Command) and
      (CompareText(Command, '"' + ExpandConstant('{app}\SnappySnap.exe') + '" --tray') = 0) then
      if not RegDeleteValue(HKCU, RunKey, 'SnappySnap') then RaiseLastException;
  end;
end;

procedure DeinitializeUninstall();
begin
  ReleaseMaintenance();
end;
