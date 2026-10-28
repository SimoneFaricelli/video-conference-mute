; VideoConference Mute installer (Inno Setup 6).
; Built by build/build.sh from out/app; see README.md.

#define AppName "VideoConference Mute"
#define AppVersion "1.0.0"
#define AppExe "VCM.exe"
#define SourceDir "..\out\app"

[Setup]
AppId={{6B0E5B8C-3F2D-4C8E-9C3A-8E0F2B7D4A11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=VCM
DefaultDirName={autopf}\VCM
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Registering the virtual camera media source (HKLM) needs administrator rights, once.
PrivilegesRequired=admin
MinVersion=10.0.19041
OutputDir=..\out
OutputBaseFilename=VCM-Setup-{#AppVersion}
SetupIconFile=..\src\Vcm.App\Assets\VCM.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[CustomMessages]
english.AutoStart=Start VideoConference Mute when Windows starts
italian.AutoStart=Avvia VideoConference Mute all'avvio di Windows
english.RuntimeMissing=The .NET 10 Desktop Runtime could not be installed. Install it from https://dotnet.microsoft.com and run setup again.
italian.RuntimeMissing=Impossibile installare .NET 10 Desktop Runtime. Installalo da https://dotnet.microsoft.com ed esegui di nuovo l'installazione.
english.InstallingRuntime=Installing the .NET 10 Desktop Runtime...
italian.InstallingRuntime=Installazione di .NET 10 Desktop Runtime...

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; Log folder written by the media source running inside the Frame Server service.
Name: "{commonappdata}\VCM"; Permissions: everyone-modify

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.dll"; Excludes: "VcmVirtualCamera.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion
; The Frame Server may hold the DLL loaded: replace it after a reboot if needed.
Source: "{#SourceDir}\VcmVirtualCamera.dll"; DestDir: "{app}"; Flags: ignoreversion regserver restartreplace uninsrestartdelete
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VCM"; \
    ValueData: """{app}\{#AppExe}"" --background"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Lets a running instance unmute microphone and camera before it is removed.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "StopVcm"

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\VCM"

[Code]
var
  DownloadPage: TDownloadWizardPage;

function IsDesktopRuntimeInstalled(): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), FindRec);
  if Result then
    FindClose(FindRec);
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or IsDesktopRuntimeInstalled() then
    Exit;

  DownloadPage.Clear;
  DownloadPage.Add('https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', 'windowsdesktop-runtime-win-x64.exe', '');
  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DownloadPage.SetText(CustomMessage('InstallingRuntime'), '');
      Exec(ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe'), '/install /quiet /norestart', '', SW_SHOW,
        ewWaitUntilTerminated, ResultCode);
    except
      Log(GetExceptionMessage);
    end;
  finally
    DownloadPage.Hide;
  end;

  if not IsDesktopRuntimeInstalled() then
  begin
    SuppressibleMsgBox(CustomMessage('RuntimeMissing'), mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  ExistingExe: String;
begin
  // Upgrade: close the running app gracefully so it unmutes devices and releases its files.
  ExistingExe := ExpandConstant('{app}\{#AppExe}');
  if FileExists(ExistingExe) then
    ExecAsOriginalUser(ExistingExe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;
