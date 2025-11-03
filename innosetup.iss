#define MyAppName "FO Dev Manager"
#define MyAppVersion "0.9.1	"
#define MyAppPublisher "ECIT Peritus AS"
#define MyAppURL "https://www.ecit.com/no/ecit-peritus/"
#define MyAppExeName "FODevManager.WinUI.exe"

[Setup]
AppId={{572DE851-82D6-430F-B12A-EBF141025C02}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
; Uncomment the following line to run in non administrative install mode (install for current user only).
;PrivilegesRequired=lowest
OutputBaseFilename=FODevManagerSetup
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "C:\dev\FODevManager\FODevManager.WinUI\bin\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Code]
var
  SourceDir: string;
  ConfigPage: TInputQueryWizardPage;
  ShouldConfigure: Boolean;

function LoadTextFile(const FileName: string): string;
var
  S: AnsiString;
begin
  if LoadStringFromFile(FileName, S) then
    Result := S
  else
    Result := '';
end;

procedure InitializeWizard();
begin
  // Always create the config page
  ConfigPage := CreateInputQueryPage(
    wpSelectDir, 'Configuration', 'First-Time Setup',
    'Enter the source directory for your repositories.'
  );
  ConfigPage.Add('Source Directory:', False);
  ConfigPage.Values[0] := 'C:\Users\localadmin\source\repos';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
var
  ConfigFilePath: string;
begin
  Result := False;

  if Assigned(ConfigPage) and (PageID = ConfigPage.ID) then
  begin
    ConfigFilePath := ExpandConstant('{app}\appsettings.json');
    ShouldConfigure := not FileExists(ConfigFilePath);

    if not ShouldConfigure then
    begin
      Result := True; // skip this page
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if Assigned(ConfigPage) and (CurPageID = ConfigPage.ID) then
  begin
    SourceDir := ConfigPage.Values[0];
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  JsonPath, JsonContent: string;
begin
  if (CurStep = ssPostInstall) and ShouldConfigure then
  begin
    JsonPath := ExpandConstant('{app}\appsettings.json');
    JsonContent := LoadTextFile(ExpandConstant('{app}\appsettings.template.json'));
    StringChangeEx(JsonContent, '{{SourceDir}}', SourceDir, True);
    SaveStringToFile(JsonPath, JsonContent, False);
    MsgBox('✅ appsettings.json created successfully.', mbInformation, MB_OK);
  end;
end;


[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

