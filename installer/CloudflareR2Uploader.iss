#ifndef AppVersion
  #error AppVersion must be provided by Build-Release.ps1
#endif
#ifndef NumericVersion
  #error NumericVersion must be provided by Build-Release.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be provided by Build-Release.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be provided by Build-Release.ps1
#endif
#ifndef OutputBaseFilename
  #error OutputBaseFilename must be provided by Build-Release.ps1
#endif
#ifndef RepositoryRoot
  #error RepositoryRoot must be provided by Build-Release.ps1
#endif

[Setup]
AppId={{C6995E9D-C62B-4904-A43A-C51A87EBF8AE}
AppName=Cloudflare R2 Uploader
AppVersion={#AppVersion}
AppVerName=Cloudflare R2 Uploader {#AppVersion}
AppPublisher=Saboreq
AppPublisherURL=https://github.com/Saboreq/CloudflareR2Uploader
AppSupportURL=https://github.com/Saboreq/CloudflareR2Uploader/issues
AppUpdatesURL=https://github.com/Saboreq/CloudflareR2Uploader/releases
DefaultDirName={localappdata}\Programs\CloudflareR2Uploader
DefaultGroupName=Cloudflare R2 Uploader
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#RepositoryRoot}\src\CloudflareR2Uploader.Wpf\Resources\app.ico
UninstallDisplayIcon={app}\CloudflareR2Uploader.exe
UninstallDisplayName=Cloudflare R2 Uploader
Uninstallable=yes
CreateUninstallRegKey=yes
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
CloseApplicationsFilter=CloudflareR2Uploader.exe
RestartApplications=no
UsePreviousAppDir=yes
VersionInfoVersion={#NumericVersion}
VersionInfoCompany=Saboreq
VersionInfoDescription=Cloudflare R2 Uploader Setup
VersionInfoProductName=Cloudflare R2 Uploader
VersionInfoProductVersion={#NumericVersion}
VersionInfoCopyright=

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Cloudflare R2 Uploader"; Filename: "{app}\CloudflareR2Uploader.exe"; WorkingDir: "{app}"
Name: "{group}\Uninstall Cloudflare R2 Uploader"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Cloudflare R2 Uploader"; Filename: "{app}\CloudflareR2Uploader.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\CloudflareR2Uploader.exe"; Parameters: "--show"; Description: "Open Cloudflare R2 Uploader"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetDesktopRuntimeInstalled: Boolean;
var
  Versions: TArrayOfString;
  Index: Integer;
begin
  Result := False;
  { The .NET installer stores this registration in the 32-bit registry view;
    the x64 segment identifies the runtime architecture. Keep the 64-bit-view
    fallback for machines whose registration was created by another installer. }
  if not RegGetValueNames(
    HKLM32,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    Versions) then
    if not RegGetValueNames(
      HKLM64,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      Versions) then
      Exit;

  for Index := 0 to GetArrayLength(Versions) - 1 do
    if Pos('10.', Versions[Index]) = 1 then
    begin
      Result := True;
      Exit;
    end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := IsDotNetDesktopRuntimeInstalled;
  if Result then
    Exit;

  MsgBox(
    'Cloudflare R2 Uploader requires the Microsoft .NET 10 Desktop Runtime (x64). ' +
    'Install it, then run this setup again.',
    mbError,
    MB_OK);

  if not WizardSilent then
    ShellExec(
      'open',
      'https://dotnet.microsoft.com/download/dotnet/10.0',
      '',
      '',
      SW_SHOWNORMAL,
      ewNoWait,
      ErrorCode);
end;

function IsUpdateInstall: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:UPDATE|0}'), '1') = 0;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardSilent and IsUpdateInstall then
    Exec(ExpandConstant('{app}\CloudflareR2Uploader.exe'), '--show', ExpandConstant('{app}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
end;
