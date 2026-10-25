; El instalador de VibeScreen para Windows.
;
; No se compila a mano: lo hace packaging/windows/build.ps1, que antes publica la ventana y el
; servicio, genera el icono y descarga PawnIO, y le pasa la versión y dónde ha dejado cada cosa.
;
;   ./packaging/windows/build.ps1 -Version 0.3.0
;
; Hace falta Inno Setup 7: el instalador es de 64 bits (SetupArchitecture), así que {sys} es el
; System32 de verdad y sc.exe, net.exe y taskkill.exe se llaman sin redirecciones de por medio.

#if Ver < EncodeVer(7, 0, 0)
  #error Hace falta Inno Setup 7 o posterior
#endif

#ifndef AppVersion
  #error Falta la versión. Compílalo con build.ps1, o con ISCC /DAppVersion=x.y.z
#endif

; La de los metadatos del ejecutable, que solo admite números: 0.3.5-dev.12 se queda en 0.3.5.
#ifndef NumericVersion
  #define NumericVersion AppVersion
#endif

#ifndef Artifacts
  #define Artifacts "..\..\artifacts"
#endif

#define Icons Artifacts + "\icon"

[Setup]
; Fijo para siempre: es lo que hace que instalar una versión nueva sea actualizar la que hay.
AppId={{1B91D0C8-4657-4FF3-BA4D-BEFA94557BE1}
AppName=VibeScreen
AppVersion={#AppVersion}
AppPublisher=Sergio Sahuquillo
AppPublisherURL=https://github.com/sergiosahuquillo/vibescreen
AppSupportURL=https://github.com/sergiosahuquillo/vibescreen/issues
AppUpdatesURL=https://github.com/sergiosahuquillo/vibescreen/releases
VersionInfoVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}

; Por máquina y como administrador, y no hay otra forma: el servicio corre como LocalSystem y PawnIO
; es un controlador de kernel.
PrivilegesRequired=admin
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809, lo mínimo que pide PawnIO.
MinVersion=10.0.17763

DefaultDirName={autopf}\VibeScreen
DisableProgramGroupPage=yes
UninstallDisplayName=VibeScreen
UninstallDisplayIcon={app}\vibescreen-app.exe
LicenseFile=..\..\LICENSE

; Claro u oscuro según Windows, como la propia ventana.
WizardStyle=modern dynamic
SetupIconFile={#Icons}\vibescreen.ico
WizardImageFile={#Icons}\wizard-100.png,{#Icons}\wizard-125.png,{#Icons}\wizard-150.png,{#Icons}\wizard-175.png,{#Icons}\wizard-200.png
WizardSmallImageFile={#Icons}\wizard-small-100.png,{#Icons}\wizard-small-125.png,{#Icons}\wizard-small-150.png,{#Icons}\wizard-small-175.png,{#Icons}\wizard-small-200.png

; La ventana y el servicio llevan cada uno su runtime de .NET, casi idénticos. Con un diccionario
; mayor que lo que ocupa la ventana entera, el compresor ve la segunda copia como una repetición de la
; primera y apenas suma a la descarga. Cuesta memoria al compilar, no al instalar.
Compression=lzma2/ultra64
SolidCompression=yes
LZMADictionarySize=262144

OutputDir={#Artifacts}\installer
OutputBaseFilename=VibeScreen-{#AppVersion}-setup

; La ventana se cierra a la bandeja en vez de terminar, así que pedirle que se cierre no sirve: se
; cierra a la fuerza en PrepareToInstall, junto con el servicio.
CloseApplications=no
SetupLogging=yes

; Sin preguntar el idioma: el de Windows si es uno de los dos, y si no, el primero de la lista, que es
; el inglés. Lo mismo que hace la ventana.
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
english.PawnIOTitle=Sensor driver
english.PawnIOSubtitle=VibeScreen also installs PawnIO to read your hardware sensors.
english.PawnIOWillInstall=To read the CPU temperature and the motherboard sensors, VibeScreen needs low-level access to the hardware, and Windows only grants that through a driver.%n%nThat driver is PawnIO: open source, signed by Microsoft, and shared with other monitoring tools such as LibreHardwareMonitor and HWiNFO. It is installed together with VibeScreen; there is nothing else you need to do.%n%nIf you uninstall VibeScreen, PawnIO stays, because other programs may be using it. You can remove it separately from Installed apps.
english.PawnIOAlreadyInstalled=To read the CPU temperature and the motherboard sensors, VibeScreen uses PawnIO, a driver signed by Microsoft that other monitoring tools share as well.%n%nPawnIO %1 is already installed on this computer, so VibeScreen will use it as it is.
english.ReadyPawnIO=Sensor driver:
english.ReadyPawnIOLine=PawnIO, signed by Microsoft
english.ReadyService=Windows service:
english.ReadyServiceLine=VibeScreen, starts with Windows and keeps the screen running
english.InstallingPawnIO=Installing the PawnIO sensor driver...
english.StartingService=Starting the VibeScreen service...
english.PawnIOFailed=PawnIO could not be installed (error %1).%n%nVibeScreen will work, but without the CPU temperature and the motherboard sensors. You can install PawnIO yourself from pawnio.eu.
english.ServiceDescription=Keeps the VibeScreen USB screen connected and draws the sensors on it.
english.DeleteData=Do you also want to delete your themes and settings?%n%nIf you are going to install VibeScreen again, choose No and they will still be there.

spanish.PawnIOTitle=Controlador de sensores
spanish.PawnIOSubtitle=VibeScreen instala también PawnIO para leer los sensores del equipo.
spanish.PawnIOWillInstall=Para leer la temperatura de la CPU y los sensores de la placa base, VibeScreen necesita acceder al hardware a bajo nivel, y Windows solo lo permite a través de un controlador.%n%nEse controlador es PawnIO: de código abierto, firmado por Microsoft y compartido con otros programas de monitorización como LibreHardwareMonitor y HWiNFO. Se instala junto con VibeScreen; no tienes que hacer nada más.%n%nSi desinstalas VibeScreen, PawnIO se queda, porque puede haber otros programas usándolo. Se puede quitar aparte desde Aplicaciones instaladas.
spanish.PawnIOAlreadyInstalled=Para leer la temperatura de la CPU y los sensores de la placa base, VibeScreen usa PawnIO, un controlador firmado por Microsoft que comparten también otros programas de monitorización.%n%nEn este equipo ya está instalado PawnIO %1, así que VibeScreen usará ese tal cual.
spanish.ReadyPawnIO=Controlador de sensores:
spanish.ReadyPawnIOLine=PawnIO, firmado por Microsoft
spanish.ReadyService=Servicio de Windows:
spanish.ReadyServiceLine=VibeScreen, arranca con Windows y mantiene la pantalla en marcha
spanish.InstallingPawnIO=Instalando el controlador de sensores PawnIO...
spanish.StartingService=Arrancando el servicio de VibeScreen...
spanish.PawnIOFailed=No se ha podido instalar PawnIO (error %1).%n%nVibeScreen funcionará, pero sin la temperatura de la CPU ni los sensores de la placa base. Puedes instalar PawnIO tú mismo desde pawnio.eu.
spanish.ServiceDescription=Mantiene conectada la pantalla USB de VibeScreen y pinta en ella los sensores.
spanish.DeleteData=¿Quieres borrar también tus temas y ajustes?%n%nSi vas a volver a instalar VibeScreen, elige No y seguirán ahí.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; La ventana no va elevada y guarda los temas aquí. Sin este permiso no podría tocar lo que deja el
; instalador como administrador —el tema de serie—, ni los temas que haya guardado otro usuario.
Name: "{commonappdata}\VibeScreen\layouts"; Permissions: users-modify; Flags: uninsneveruninstall

[Files]
; Cada uno en su carpeta y tal cual sale de dotnet publish. Juntos no pueden ir: la ventana es net10.0
; y el servicio net10.0-windows, y cada uno trae un VibeScreen.Core.dll distinto con el mismo nombre.
Source: "{#Artifacts}\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#Artifacts}\publish\service\*"; DestDir: "{app}\service"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\..\src\VibeScreen.App\Assets\Fonts\*-OFL.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

; El tema de serie, solo la primera vez y sin pisar uno que ya esté: si el usuario lo cambia o lo
; borra, una actualización no se lo devuelve. Tampoco se lleva al desinstalar; eso lo pregunta el
; desinstalador junto con el resto de los temas.
Source: "..\themes\VibeScreen\*"; DestDir: "{commonappdata}\VibeScreen\layouts\VibeScreen"; Check: IsFirstInstall; Flags: onlyifdoesntexist uninsneveruninstall recursesubdirs

; Se extrae a mano, solo si hace falta.
Source: "{#Artifacts}\pawnio\PawnIO_setup.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\VibeScreen"; Filename: "{app}\vibescreen-app.exe"
Name: "{autodesktop}\VibeScreen"; Filename: "{app}\vibescreen-app.exe"; Tasks: desktopicon

[Run]
; Sin elevar: si la ventana heredara el token del instalador, Windows le rompería el arrastrar y
; soltar desde el Explorador. Es lo mismo que pide su app.manifest.
Filename: "{app}\vibescreen-app.exe"; Description: "{cm:LaunchProgram,VibeScreen}"; Flags: postinstall nowait skipifsilent runasoriginaluser
; La actualización desde la propia ventana: la lanza en silencio, y StopServiceAndApp la cierra. Pide
; volver a abrirla con /RELAUNCH=1, porque en silencio la casilla de arriba no sale y nadie lo haría.
; postinstall aunque no haya casilla: es lo que la deja para el final, con el servicio ya arrancado.
; Sin él va antes de ssPostInstall, y la ventana se abría diez segundos antes que el servicio.
Filename: "{app}\vibescreen-app.exe"; Flags: postinstall nowait runasoriginaluser; Check: RelaunchAfterUpdate

[Code]
const
  ServiceName = 'VibeScreen';
  PawnIOKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO';
  OwnKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{1B91D0C8-4657-4FF3-BA4D-BEFA94557BE1}_is1';
  RebootRequired = 3010;

var
  PawnIOPage: TOutputMsgWizardPage;
  PawnIOVersion: String;
  FirstInstall: Boolean;
  RestartNeeded: Boolean;
  DeleteData: Boolean;

{ Lanza un programa sin ventana, espera a que termine y devuelve su código de salida. Todo queda en el
  registro del instalador, que es lo que hay que pedir cuando alguien cuenta que algo no le ha ido. }
function RunHidden(const FileName, Params: String): Integer;
var
  Code: Integer;
begin
  if not Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Code := -1;
  Log(Format('%s %s -> %d', [FileName, Params, Code]));
  Result := Code;
end;

function Sc(const Params: String): Integer;
begin
  Result := RunHidden(ExpandConstant('{sys}\sc.exe'), Params);
end;

{ La versión de PawnIO que hay instalada, o vacío si no hay ninguna. Su instalador se niega a
  instalar encima de otra versión, así que con una ya puesta no se toca. }
function InstalledPawnIO: String;
var
  Version: String;
begin
  Result := '';
  if not RegKeyExists(HKLM64, PawnIOKey) then
    Exit;

  if RegQueryStringValue(HKLM64, PawnIOKey, 'DisplayVersion', Version) and (Version <> '') then
    Result := Version
  else
    Result := '?';
end;

function IsFirstInstall: Boolean;
begin
  Result := FirstInstall;
end;

{ Solo en silencio: con el asistente a la vista ya está la casilla de «Abrir VibeScreen». }
function RelaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;

function InitializeSetup: Boolean;
begin
  PawnIOVersion := InstalledPawnIO;
  FirstInstall := not RegKeyExists(HKLM64, OwnKey);
  Result := True;
end;

procedure InitializeWizard;
var
  Text: String;
begin
  if PawnIOVersion = '' then
    Text := CustomMessage('PawnIOWillInstall')
  else
    Text := FmtMessage(CustomMessage('PawnIOAlreadyInstalled'), [PawnIOVersion]);

  PawnIOPage := CreateOutputMsgPage(wpLicense,
    CustomMessage('PawnIOTitle'), CustomMessage('PawnIOSubtitle'), Text);
end;

{ Al actualizar con PawnIO ya puesto no hay nada que contar: no se instala nada nuevo. }
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PawnIOPage.ID) and (PawnIOVersion <> '') and not FirstInstall;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;

  Result := Result + NewLine + NewLine + CustomMessage('ReadyService') + NewLine + Space +
    CustomMessage('ReadyServiceLine');

  if PawnIOVersion = '' then
    Result := Result + NewLine + NewLine + CustomMessage('ReadyPawnIO') + NewLine + Space +
      CustomMessage('ReadyPawnIOLine');
end;

{ El servicio y la ventana sueltan sus ficheros. net stop espera a que el servicio pare de verdad, y
  sc stop no: copiar encima de un ejecutable que todavía corre falla. La ventana, a la fuerza, porque
  pedirle que se cierre la manda a la bandeja. }
procedure StopServiceAndApp;
begin
  RunHidden(ExpandConstant('{sys}\net.exe'), 'stop ' + ServiceName);
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM vibescreen-app.exe');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceAndApp;
  Result := '';
end;

procedure InstallPawnIO;
var
  Code: Integer;
begin
  if PawnIOVersion <> '' then
    Exit;

  WizardForm.StatusLabel.Caption := CustomMessage('InstallingPawnIO');
  ExtractTemporaryFile('PawnIO_setup.exe');
  Code := RunHidden(ExpandConstant('{tmp}\PawnIO_setup.exe'), '-install -silent');

  { Desde la 2.2, en silencioso devuelve ERROR_SUCCESS_REBOOT_REQUIRED cuando hace falta reiniciar.
    Cualquier otro error no detiene la instalación: sin PawnIO la aplicación funciona, solo que sin
    la temperatura de la CPU ni los sensores de la placa, y eso se le cuenta al usuario. }
  if Code = RebootRequired then
    RestartNeeded := True
  else if Code <> 0 then
    SuppressibleMsgBox(FmtMessage(CustomMessage('PawnIOFailed'), [IntToStr(Code)]), mbError, MB_OK, IDOK);
end;

procedure RegisterService;
var
  BinPath: String;
begin
  WizardForm.StatusLabel.Caption := CustomMessage('StartingService');

  { La ruta entre comillas escapadas: con espacios en «Program Files» y sin ellas, el gestor de
    servicios probaría a ejecutar C:\Program.exe. }
  BinPath := 'binPath= "\"' + ExpandConstant('{app}\service\vibescreen-service.exe') + '\""';

  { Si ya existe, de una versión anterior, se reconfigura: la carpeta puede haber cambiado. }
  if Sc('query ' + ServiceName) = 0 then
    Sc('config ' + ServiceName + ' ' + BinPath + ' start= auto')
  else
    Sc('create ' + ServiceName + ' ' + BinPath + ' start= auto DisplayName= "VibeScreen"');

  Sc('description ' + ServiceName + ' "' + CustomMessage('ServiceDescription') + '"');

  { Si se cae, que vuelva: a los 5 s, a los 10 s y luego cada minuto, y el contador se pone a cero
    cada día. Un servicio que el gestor da por parado deja el panel congelado hasta reiniciar. }
  Sc('failure ' + ServiceName + ' reset= 86400 actions= restart/5000/restart/10000/restart/60000');

  RunHidden(ExpandConstant('{sys}\net.exe'), 'start ' + ServiceName);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  { PawnIO antes que el servicio: LibreHardwareMonitor busca el controlador al arrancar, y si no lo
    encuentra no vuelve a mirar hasta el siguiente arranque. }
  if CurStep = ssPostInstall then
  begin
    InstallPawnIO;
    RegisterService;
  end;
end;

function NeedRestart: Boolean;
begin
  Result := RestartNeeded;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  case CurUninstallStep of
    usUninstall:
      begin
        { Después de que el usuario haya confirmado que desinstala, no antes. En silencioso no se
          pregunta y se conserva: borrar datos es lo único que no tiene vuelta atrás. }
        if not UninstallSilent then
          DeleteData := MsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

        StopServiceAndApp;
        Sc('delete ' + ServiceName);
      end;

    { PawnIO no se quita: lo comparten otros programas, y se puede desinstalar aparte. }
    usPostUninstall:
      if DeleteData then
      begin
        DelTree(ExpandConstant('{commonappdata}\VibeScreen'), True, True, True);
        DelTree(ExpandConstant('{userappdata}\VibeScreen'), True, True, True);
      end;
  end;
end;
