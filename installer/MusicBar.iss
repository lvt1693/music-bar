; MusicBar installer (Inno Setup 6). Built by installer\build.ps1, which passes the version and
; the folder that holds the self-contained publish output:
;   ISCC.exe /DAppVersion=1.0.0 /DPublishDir=..\artifacts\publish installer\MusicBar.iss
;
; Per-user install, no admin prompt. Before files are replaced or removed, the running MusicBar is
; asked to quit with --exit, so it hands the taskbar and the tray back exactly as Windows had them.
; Uninstalling then runs MusicBar --uninstall, which puts back the Windows settings MusicBar changed
; (taskbar mode, size, alignment) to how they were before it first ran, unless changed again since.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{6C1F7D5E-3B7A-4F0E-9A51-7D2C3E8B4A90}
AppName=MusicBar
AppVersion={#AppVersion}
AppVerName=MusicBar {#AppVersion}
AppPublisher=Truc
AppPublisherURL=https://github.com/lvt1693/music-bar
AppSupportURL=https://github.com/lvt1693/music-bar/issues
DefaultDirName={localappdata}\Programs\MusicBar
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
UninstallDisplayName=MusicBar
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
vi.LaunchNow=Mở MusicBar ngay
en.LaunchNow=Start MusicBar now

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\MusicBar.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MusicBar"; Filename: "{app}\MusicBar.exe"
Name: "{autodesktop}\MusicBar"; Filename: "{app}\MusicBar.exe"; Tasks: desktopicon

[Registry]
; MusicBar writes this itself when "Khởi động cùng Windows" is on; the uninstaller takes it away.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "MusicBar"; ValueType: none; Flags: uninsdeletevalue

[Run]
; --updated: the new MusicBar restarts Explorer if Explorer still holds the old copy's taskbar helper.
Filename: "{app}\MusicBar.exe"; Parameters: "--updated"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent
; An update started from inside MusicBar runs silently: open the new version when it is done.
Filename: "{app}\MusicBar.exe"; Parameters: "--updated"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\MusicBar.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated; RunOnceId: "PutWindowsBack"

[Code]
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
