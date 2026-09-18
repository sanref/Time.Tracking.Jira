; Time.Tracking.Jira - Inno Setup Script
; Requires Inno Setup 6.1+ (https://jrsoftware.org/isinfo.php)
;
; The same package installs from scratch and updates an existing installation:
;
;   * AppId must never change. It is what lets Setup find a previous install and
;     reuse its folder, Start Menu group and icon choices instead of installing
;     a second copy somewhere else. It is independent of the .NET runtime, which
;     is why the move from .NET Framework to .NET 10 is still an ordinary update
;     and not a reinstall.
;   * Nothing under the user profile is written or deleted, so the Jira
;     connection, the tracked issues and every preference survive an update.
;     Since 4.1 settings live in
;         %LOCALAPPDATA%\Seabury Solutions\Time.Tracking.Jira\settings.json
;     Earlier versions kept them in a user.config under a folder .NET named after
;     the executable and its version; the application imports that one on its
;     first run (see UserConfigImporter) and leaves it untouched, so downgrading
;     back to 4.0.x still finds its own configuration.
;
; The application is framework-dependent: it needs the .NET Desktop Runtime. Setup
; checks for it and offers to download it when it is missing, so a machine that
; never had .NET installed still updates in one step.
;
; Unattended update (no UI, closes a running instance by itself):
;     Time.Tracking.Jira-<version>-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART

#define AppName      "Time.Tracking.Jira"
#define AppExeName   "Time.Tracking.Jira.exe"
#define AppId        "{A1B2C3D4-E5F6-4A5B-8C9D-0E1F2A3B4C5D}"
#define AppMutexName "{D5597999-20FE-430F-8E5D-8893EBED2599}"
#define SourceDir    "..\Time.Tracking.Jira\bin\Release\net10.0-windows\win-x64\publish"
; Major minimo del .NET Desktop Runtime. El csproj usa RollForward=LatestMajor, asi
; que un major mayor tambien sirve.
#define DotNetMajor  10
#define DotNetUrl    "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
; GetVersionNumbersString y no GetFileVersion: Inno Setup 6.7 renombro esa funcion y la
; vieja ya avisa que esta obsoleta. Devuelven lo mismo, "4.1.1.0".
#define _FullVer     GetVersionNumbersString(SourceDir + "\" + AppExeName)
#define AppVersion   Copy(_FullVer, 1, RPos(".", _FullVer) - 1)
#define AppPublisher "Seabury Solutions"
#define AppURL       "https://www.seaburymro.com"
#define AppIconFile  "..\Time.Tracking.Jira\stopwatchicon.ico"

[Setup]
AppId={{#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
VersionInfoVersion={#_FullVer}
DefaultDirName={autopf}\{#AppPublisher}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=License.rtf
OutputDir=Output
OutputBaseFilename=Time.Tracking.Jira-{#AppVersion}-Setup
SetupIconFile={#AppIconFile}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName} {#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; Solo 64 bits: la aplicacion se publica para win-x64. Antes se permitia x86 porque
; .NET Framework corria en cualquier arquitectura.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; .NET 10 no soporta Windows 7 ni 8.1: el minimo es Windows 10 1607 / Server 2016
MinVersion=10.0.14393

; ---- Update behaviour ----
; Reuse what the previous installation chose so an update never asks twice
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
UsePreviousSetupType=yes
DisableDirPage=auto
DisableProgramGroupPage=auto
; The welcome page is where the update is announced, so it stays visible
DisableWelcomePage=no
; Ask a running Time.Tracking.Jira to close instead of failing on locked files.
; Restart Manager closes it politely, so the app still saves its rows and timers.
CloseApplications=yes
; Do not let Restart Manager relaunch it: Setup runs elevated and would restart
; the app elevated too. The [Run] entry below launches it as the real user.
RestartApplications=no
AppMutex={{#AppMutexName}
SetupMutex={#AppName}-Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";     Description: "{cm:CreateDesktopIcon}";     GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; Se limpia el directorio de programa antes de copiar, no el perfil del usuario:
; una instalacion sobre .NET Framework deja un Time.Tracking.Jira.exe.config que la
; aplicacion ya no usa, y los ensamblados pueden cambiar de nombre entre versiones.
; No hay [UninstallDelete]: nada del perfil del usuario se borra nunca.
[InstallDelete]
Type: files; Name: "{app}\{#AppExeName}.config"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"

; La publicacion de .NET son varios archivos: el apphost, el ensamblado gestionado,
; runtimeconfig.json, deps.json y las dependencias.
[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{autoprograms}\Microsoft\Internet Explorer\Quick Launch\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: quicklaunchicon

[Run]
; runasoriginaluser: Setup is elevated, but the app must run as the logged on
; user, otherwise its settings would land in the administrator's profile
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppId}_is1';

var
  PreviousVersion: String;

function ReadDisplayVersion(RootKey: Integer): String;
begin
  Result := '';
  RegQueryStringValue(RootKey, UninstallKey, 'DisplayVersion', Result);
end;

{ Version of the installation already on this machine, empty when there is none.
  Both registry views are checked so an update finds a 32 bit install too. }
function DetectPreviousVersion(): String;
begin
  Result := '';
  if IsWin64 then
    Result := ReadDisplayVersion(HKLM64);
  if Result = '' then
    Result := ReadDisplayVersion(HKLM32);
  if Result = '' then
    Result := ReadDisplayVersion(HKCU);
end;

function IsUpdate(): Boolean;
begin
  Result := PreviousVersion <> '';
end;

{ ---- .NET Desktop Runtime ---- }

{ El major de un nombre de carpeta como "10.0.11" }
function MajorOf(Name: String): Integer;
var
  Dot: Integer;
begin
  Dot := Pos('.', Name);
  if Dot > 1 then
    Result := StrToIntDef(Copy(Name, 1, Dot - 1), 0)
  else
    Result := 0;
end;

{ El runtime se busca por carpeta y no por registro: es el mismo lugar donde lo
  busca el propio ejecutable, asi que no puede dar un falso positivo por una clave
  que quedo de una desinstalacion. }
function DesktopRuntimeInstalled(): Boolean;
var
  Root: String;
  FindRec: TFindRec;
begin
  Result := False;
  Root := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if not DirExists(Root) then
    Exit;

  if FindFirst(Root + '\*', FindRec) then
  try
    repeat
      if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        if MajorOf(FindRec.Name) >= {#DotNetMajor} then
        begin
          Result := True;
          Exit;
        end;
    until not FindNext(FindRec);
  finally
    FindClose(FindRec);
  end;
end;

{ Descarga e instala el runtime. Devuelve '' si quedo instalado, o el motivo del fallo. }
function InstallDesktopRuntime(): String;
var
  Installer: String;
  ResultCode: Integer;
begin
  Installer := ExpandConstant('{tmp}\windowsdesktop-runtime.exe');

  try
    DownloadTemporaryFile('{#DotNetUrl}', 'windowsdesktop-runtime.exe', '', nil);
  except
    Result := 'No se pudo descargar el .NET Desktop Runtime {#DotNetMajor}:' + #13#10 +
              GetExceptionMessage;
    Exit;
  end;

  if not Exec(Installer, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'No se pudo ejecutar el instalador del .NET Desktop Runtime {#DotNetMajor}.';
    Exit;
  end;

  { 3010 = instalado, pide reinicio }
  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    Result := 'La instalacion del .NET Desktop Runtime {#DotNetMajor} termino con el codigo ' +
              IntToStr(ResultCode) + '.';
    Exit;
  end;

  Result := '';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if DesktopRuntimeInstalled() then
    Exit;

  Result := InstallDesktopRuntime();

  if Result <> '' then
    Result := Result + #13#10#13#10 +
      '{#AppName} necesita el .NET Desktop Runtime {#DotNetMajor} (64 bits). ' +
      'Instalalo manualmente desde https://dotnet.microsoft.com/download/dotnet/{#DotNetMajor}.0 ' +
      'y volve a ejecutar este instalador.';
end;

function InitializeSetup(): Boolean;
var
  Installed, Packaged: Int64;
begin
  Result := True;
  PreviousVersion := DetectPreviousVersion();

  { Avisar antes de empezar, para que nadie se sorprenda con una descarga. En modo
    silencioso no se pregunta: la actualizacion desatendida tiene que poder completarse
    sola, que es justamente para lo que se usa. }
  if not DesktopRuntimeInstalled() and not WizardSilent() then
  begin
    Result := MsgBox('{#AppName} necesita el .NET Desktop Runtime {#DotNetMajor}, que no esta instalado ' +
      'en este equipo.' + #13#10#13#10 +
      'Setup puede descargarlo e instalarlo ahora (unos 60 MB). ¿Continuar?',
      mbConfirmation, MB_YESNO) = IDYES;

    if not Result then
      Exit;
  end;

  if not IsUpdate() then
    Exit;

  if not StrToVersion(PreviousVersion, Installed) then
    Exit;
  if not StrToVersion('{#AppVersion}', Packaged) then
    Exit;

  if ComparePackedVersion(Installed, Packaged) > 0 then
    Result := MsgBox('{#AppName} ' + PreviousVersion + ' is already installed, which is newer than ' +
      'the {#AppVersion} in this package.'#13#10#13#10 +
      'Install the older version anyway? Your settings are kept either way.',
      mbConfirmation, MB_YESNO) = IDYES;
end;

procedure InitializeWizard();
begin
  if not IsUpdate() then
    Exit;

  WizardForm.WelcomeLabel1.Caption := 'Update ' + '{#AppName}';
  WizardForm.WelcomeLabel2.Caption :=
    '{#AppName} ' + PreviousVersion + ' is installed and will be updated to {#AppVersion}.'#13#10#13#10 +
    'Your Jira connection, tracked issues and preferences are kept: only the program files are replaced.'#13#10#13#10 +
    'If {#AppName} is running, Setup closes it before continuing.';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  { The license was already accepted by the installation being updated }
  Result := IsUpdate() and (PageID = wpLicense);
end;
