; MusicBar installer (Inno Setup 6). Built by installer\build.ps1, which passes the version and
; the folder that holds the self-contained publish output:
;   ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\artifacts\publish installer\MusicBar.iss
;
; Per-user install, no admin prompt. Before files are replaced or removed, the running MusicBar is
; asked to quit with --exit, so it hands the taskbar and the tray back exactly as Windows had them.
; Uninstalling then runs MusicBar --uninstall, which puts back the Windows settings MusicBar changed
; (taskbar mode, size, alignment) to how they were before it first ran, unless changed again since.
;
; Since 2.0.1 people never see this setup's own wizard: it travels inside MusicBar's setup window
; (installer\SetupUI), which runs it hidden (/VERYSILENT) and draws its own screens. The window passes
; /HOSTWND= (where to post the progress) and /NOLAUNCH=1 (it opens MusicBar itself). With
; /DUninstallerExe= the window's uninstall copy goes beside MusicBar, and Windows' Installed apps
; opens it instead of Inno's uninstaller (which it then runs hidden).

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
; A test build's own identity (installer\SetupUI's tests): another AppId and folder name, so it never
; touches the MusicBar installed on the machine (its uninstall entry, shortcuts, Run value, files).
#ifndef AppIdValue
  #define AppIdValue "6C1F7D5E-3B7A-4F0E-9A51-7D2C3E8B4A90"
#endif
#ifndef AppFolder
  #define AppFolder "MusicBar"
#endif

[Setup]
AppId={{{#AppIdValue}}
AppName=MusicBar
AppVersion={#AppVersion}
AppVerName=MusicBar {#AppVersion}
AppPublisher=Truc
AppPublisherURL=https://github.com/lvt1693/music-bar
AppSupportURL=https://github.com/lvt1693/music-bar/issues
DefaultDirName={localappdata}\Programs\{#AppFolder}
DefaultGroupName=MusicBar
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts
OutputBaseFilename=MusicBar-{#AppVersion}-Setup
SetupIconFile=..\src\MusicBar\Assets\MusicBar.ico
UninstallDisplayIcon={app}\MusicBar.exe
UninstallDisplayName={#AppFolder}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
LicenseFile=..\LICENSE
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

[Languages]
Name: "vi"; MessagesFile: "Vietnamese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
vi.DesktopIcon=Tạo lối tắt trên màn hình
en.DesktopIcon=Create a desktop shortcut
vi.StartMenuIcon=Thêm vào menu Start
en.StartMenuIcon=Add to the Start menu
vi.LaunchNow=Mở MusicBar ngay
en.LaunchNow=Start MusicBar now

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuIcon}"
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\MusicBar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Windows 10's identity package (build.ps1 -IdentityCert), when this build has one.
Source: "{#PublishDir}\identity\*"; DestDir: "{app}\identity"; Flags: ignoreversion skipifsourcedoesntexist
#ifdef UninstallerExe
; The setup window without its installer: Installed apps opens it to uninstall (see CurStepChanged).
Source: "{#UninstallerExe}"; DestDir: "{app}"; DestName: "MusicBar-Uninstall.exe"; Flags: ignoreversion
#endif

[Icons]
Name: "{autoprograms}\{#AppFolder}"; Filename: "{app}\MusicBar.exe"; Tasks: startmenu
Name: "{autodesktop}\{#AppFolder}"; Filename: "{app}\MusicBar.exe"; Tasks: desktopicon

[Registry]
; MusicBar writes this itself when "Khởi động cùng Windows" is on; the uninstaller takes it away.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppFolder}"; ValueType: none; Flags: uninsdeletevalue

[Run]
; Windows 10, test builds only: trust the self-made certificate the identity package is signed with (an admin prompt).
Filename: "{sys}\certutil.exe"; Parameters: "-addstore TrustedPeople ""{app}\identity\MusicBar-test.cer"""; Verb: "runas"; Flags: shellexec runhidden waituntilterminated; Check: NeedsTestCertificate
; Windows 10: MusicBar's package identity, so that it may read notifications (#21). Before MusicBar opens.
Filename: "{app}\MusicBar.exe"; Parameters: "--register-identity"; Flags: runhidden waituntilterminated; Check: HasIdentityPackage
; --updated: the new MusicBar restarts Explorer if Explorer still holds the old copy's taskbar helper.
Filename: "{app}\MusicBar.exe"; Parameters: "--updated"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent
; An update started from inside MusicBar runs silently: open the new version when it is done
; (unless the setup window, which opens it itself, said /NOLAUNCH=1).
Filename: "{app}\MusicBar.exe"; Parameters: "--updated"; Flags: nowait; Check: LaunchQuietly

[UninstallRun]
Filename: "{app}\MusicBar.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "PutWindowsBack"

[Code]
function IsWindows10(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  Result := Version.Build < 22000;
end;

{ Windows 10 and a build that ships the identity package (Windows 11 lets MusicBar read notifications without it). }
function HasIdentityPackage(): Boolean;
begin
  Result := IsWindows10() and FileExists(ExpandConstant('{app}\identity\MusicBar.Identity.msix'));
end;

{ A test build's certificate, not yet in the machine's Trusted People store. }
function NeedsTestCertificate(): Boolean;
begin
#ifdef TestCertThumb
  Result := HasIdentityPackage() and FileExists(ExpandConstant('{app}\identity\MusicBar-test.cer'))
    and not RegKeyExists(HKLM, 'SOFTWARE\Microsoft\SystemCertificates\TrustedPeople\Certificates\{#TestCertThumb}');
#else
  Result := False;
#endif
end;

function MusicBarRunning(): Boolean;
var
  Locator, Wmi, Procs: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Wmi := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Wmi.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''MusicBar.exe''');
    Result := Procs.Count > 0;
  except
  end;
end;

{ Ask the running MusicBar to quit cleanly, then give it a few seconds to restore the taskbar. }
procedure ExitMusicBar(Exe: String);
var
  Code, I: Integer;
begin
  if not FileExists(Exe) or not MusicBarRunning() then
    Exit;
  Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 40 do
  begin
    if not MusicBarRunning() then
      Break;
    Sleep(150);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExitMusicBar(ExpandConstant('{app}\MusicBar.exe'));
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  ExitMusicBar(ExpandConstant('{app}\MusicBar.exe'));
  Result := True;
end;

{ Quiet installs open MusicBar when done, except under the setup window (/NOLAUNCH=1), which does it itself. }
function LaunchQuietly(): Boolean;
begin
  Result := WizardSilent() and (ExpandConstant('{param:NOLAUNCH|0}') <> '1');
end;

{ The setup window's handle (/HOSTWND=), to post the progress to; 0 when none. }
function HostWindow(): Integer;
begin
  Result := StrToIntDef(ExpandConstant('{param:HOSTWND|0}'), 0);
end;

{ The files' progress, in thousandths, as WM_APP + $51 to the setup window (Engine.ProgressMessage). }
procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
var
  Wnd: Integer;
begin
  Wnd := HostWindow();
  if (Wnd <> 0) and (MaxProgress > 0) then
    PostMessage(Wnd, $8051, Round(CurProgress * 1000.0 / MaxProgress), 0);
end;

{ Installed apps opens the setup window to uninstall (it runs this uninstaller hidden); quiet uninstalls
  (QuietUninstallString) still go straight to Inno's. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  Window: String;
begin
  if CurStep = ssPostInstall then
  begin
    Window := ExpandConstant('{app}\MusicBar-Uninstall.exe');
    if FileExists(Window) then
      RegWriteStringValue(HKA, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + '{' + '{#AppIdValue}' + '}_is1',
        'UninstallString', '"' + Window + '" /UNINSTALL');
  end;
end;
