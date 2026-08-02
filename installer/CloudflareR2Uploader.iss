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
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
SetupIconFile={#RepositoryRoot}\src\CloudflareR2Uploader\Resources\app.ico
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
