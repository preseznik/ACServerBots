#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
[Setup]
AppId={{09CB568B-40A4-4E34-8E80-06B34AC0BB46}
AppName=AssettoServer Race Control
AppVersion={#ReleaseVersion}
AppPublisher=ACServerBots contributors
AppPublisherURL=https://github.com/preseznik/ACServerBots
DefaultDirName={autopf}\AssettoServer Race Control
DefaultGroupName=AssettoServer Race Control
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputRoot}
OutputBaseFilename=RaceControl-{#ReleaseVersion}-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
LicenseFile={#RepoRoot}\LICENSE
UninstallDisplayIcon={app}\AssettoServer Race Control.exe
SetupIconFile={#RepoRoot}\AssettoServer\Assets\assettoserver.ico
CloseApplications=yes
RestartApplications=no
[Types]
Name: "racing"; Description: "Racing host (launcher + server)"
Name: "full"; Description: "Full host (launcher + server + FPS assets and maps)"
Name: "client"; Description: "FPS client files only"
Name: "custom"; Description: "Custom"; Flags: iscustom
[Components]
Name: "launcher"; Description: "Race Control launcher"; Types: racing full
Name: "server"; Description: "Matching AssettoServer fork"; Types: racing full
Name: "fpshost"; Description: "FPS assets for hosting"; Types: full
Name: "fpsclient"; Description: "FPS client files for Assetto Corsa"; Types: client
Name: "mapshost"; Description: "FPS maps and prepared arenas for hosting"; Types: full
Name: "mapsclient"; Description: "FPS maps for Assetto Corsa players"; Types: client
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Components: launcher; Flags: unchecked
[Files]
Source: "{#StageRoot}\launcher\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: launcher; Excludes: "portable.json"
Source: "{#StageRoot}\server\*"; DestDir: "{app}\lib\Server"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: server; Excludes: "portable.json"
Source: "{#StageRoot}\fps\*"; DestDir: "{app}\Packs\Fps"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: fpshost
Source: "{#StageRoot}\maps\*"; DestDir: "{app}\Packs\FpsMaps"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: mapshost
Source: "{#StageRoot}\maps\content\*"; DestDir: "{code:GetAcRoot}\content"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall; Components: mapsclient
Source: "{#StageRoot}\maps\asrc-fps-maps.json"; DestDir: "{code:GetAcRoot}"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#StageRoot}\fps\content\*"; DestDir: "{code:GetAcRoot}\content"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall; Components: fpsclient
Source: "{#StageRoot}\fps\apps\*"; DestDir: "{code:GetAcRoot}\apps"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall; Components: fpsclient
Source: "{#StageRoot}\fps\extension\*"; DestDir: "{code:GetAcRoot}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall; Components: fpsclient
Source: "{#RepoRoot}\LICENSE"; DestDir: "{code:GetAcRoot}\apps\lua\asrc_fps_hud"; Flags: ignoreversion uninsneveruninstall; Components: fpsclient
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{code:GetAcRoot}\apps\lua\asrc_fps_hud"; Flags: ignoreversion uninsneveruninstall; Components: fpsclient
Source: "{#StageRoot}\fps\asrc-fps-client.json"; DestDir: "{code:GetAcRoot}"; Flags: ignoreversion uninsneveruninstall; Components: fpsclient
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageRoot}\launcher\README.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\LICENSE"; DestDir: "{code:GetAcRoot}\content\tracks\bo2_nuketown_2020\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{code:GetAcRoot}\content\tracks\bo2_nuketown_2020\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\LICENSE"; DestDir: "{code:GetAcRoot}\content\tracks\fire_pit\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{code:GetAcRoot}\content\tracks\fire_pit\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\LICENSE"; DestDir: "{code:GetAcRoot}\content\tracks\krvava_rotunda\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{code:GetAcRoot}\content\tracks\krvava_rotunda\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\LICENSE"; DestDir: "{code:GetAcRoot}\content\tracks\shipment_1519\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
Source: "{#RepoRoot}\THIRD_PARTY_NOTICES.md"; DestDir: "{code:GetAcRoot}\content\tracks\shipment_1519\race-control"; Flags: ignoreversion uninsneveruninstall; Components: mapsclient
[Icons]
Name: "{group}\Race Control"; Filename: "{app}\AssettoServer Race Control.exe"; WorkingDir: "{app}"; Components: launcher
Name: "{autodesktop}\Race Control"; Filename: "{app}\AssettoServer Race Control.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Components: launcher
[Code]
var
  AcPage: TInputDirWizardPage;
function GetAcRoot(Param: String): String;
begin
  Result := AcPage.Values[0];
end;
procedure InitializeWizard;
begin
  AcPage := CreateInputDirPage(wpSelectComponents, 'Assetto Corsa client folder',
    'Choose the Assetto Corsa installation for the optional FPS client files and maps.',
    'FPS requires Custom Shaders Patch 0.3.0-preview520 or newer, plus the server''s track and carrier car. Selected FPS files and maps are replaced. Client files remain after uninstall; no game or CSP is installed by this setup.', False, '');
  AcPage.Add('Assetto Corsa folder:');
  AcPage.Values[0] := ExpandConstant('{param:ACROOT|{pf32}\Steam\steamapps\common\assettocorsa}');
end;
function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = AcPage.ID) and not (WizardIsComponentSelected('fpsclient') or WizardIsComponentSelected('mapsclient'));
end;
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectComponents then begin
    if not (WizardIsComponentSelected('launcher') or WizardIsComponentSelected('server') or WizardIsComponentSelected('fpsclient') or WizardIsComponentSelected('mapsclient')) then begin
      MsgBox('Select the launcher, server, FPS client files, or client maps.', mbError, MB_OK);
      Result := False;
    end;
    if (WizardIsComponentSelected('fpshost') or WizardIsComponentSelected('mapshost')) and not (WizardIsComponentSelected('launcher') or WizardIsComponentSelected('server')) then begin
      MsgBox('FPS hosting assets and maps require a launcher or server installation.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if (WizardIsComponentSelected('fpsclient') or WizardIsComponentSelected('mapsclient')) and not FileExists(AddBackslash(GetAcRoot('')) + 'acs.exe') then
    Result := 'Select an Assetto Corsa installation containing acs.exe.';
end;
