#define MyAppName "FO Dev Manager"
#define MyAppVersion "1.1.7"
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
Source: "C:\dev\source\FODevManager\FODevManager.WinUI\bin\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Code]
var
  SourceDir: string;
  ConfigPage: TInputQueryWizardPage;
  ShouldConfigure: Boolean;
  HadExistingConfig: Boolean;
  ExistingConfigBackupPath: string;

function LoadTextFile(const FileName: string): string;
var
  S: AnsiString;
begin
  if LoadStringFromFile(FileName, S) then
    Result := S
  else
    Result := '';
end;

function EscapeJsonString(const Value: string): string;
begin
  Result := Value;
  StringChangeEx(Result, '\', '\\', True);
  StringChangeEx(Result, '"', '\"', True);
end;

function ReplaceJsonStringValue(const Json, Key, NewValue: string): string;
var
  KeyToken: string;
  ValueStart: Integer;
  ValueEnd: Integer;
begin
  Result := Json;
  KeyToken := '"' + Key + '": "';
  ValueStart := Pos(KeyToken, Result);

  if ValueStart = 0 then
    RaiseException('Could not find JSON key "' + Key + '" in appsettings.json.');

  ValueStart := ValueStart + Length(KeyToken);
  ValueEnd := ValueStart;

  while (ValueEnd <= Length(Result)) and (Result[ValueEnd] <> '"') do
    ValueEnd := ValueEnd + 1;

  if ValueEnd > Length(Result) then
    RaiseException('Could not parse JSON value for key "' + Key + '" in appsettings.json.');

  Delete(Result, ValueStart, ValueEnd - ValueStart);
  Insert(EscapeJsonString(NewValue), Result, ValueStart);
end;

procedure BackupExistingConfig();
var
  ConfigPath: string;
begin
  ConfigPath := ExpandConstant('{app}\appsettings.json');
  HadExistingConfig := FileExists(ConfigPath);
  ExistingConfigBackupPath := '';

  if not HadExistingConfig then
    exit;

  ExistingConfigBackupPath := ExpandConstant('{tmp}\FODevManager.appsettings.backup.json');

  if FileExists(ExistingConfigBackupPath) then
    DeleteFile(ExistingConfigBackupPath);

  if not FileCopy(ConfigPath, ExistingConfigBackupPath, False) then
    RaiseException('Failed to back up appsettings.json before cleaning the install directory.');
end;

procedure CleanInstallDirectory();
var
  AppDir: string;
  FindRec: TFindRec;
  ItemPath: string;
begin
  AppDir := ExpandConstant('{app}');

  if not DirExists(AppDir) then
    exit;

  if FindFirst(AppDir + '\*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name = '.') or (FindRec.Name = '..') then
          continue;

        ItemPath := AppDir + '\' + FindRec.Name;

        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          if not DelTree(ItemPath, True, True, True) then
            RaiseException('Failed to remove existing directory: ' + ItemPath);
        end
        else
        begin
          if not DeleteFile(ItemPath) then
            RaiseException('Failed to remove existing file: ' + ItemPath);
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure RestoreExistingConfig();
var
  ConfigPath: string;
begin
  if not HadExistingConfig then
    exit;

  if ExistingConfigBackupPath = '' then
    exit;

  if not FileExists(ExistingConfigBackupPath) then
    RaiseException('Expected backed up appsettings.json was not found after install.');

  ConfigPath := ExpandConstant('{app}\appsettings.json');

  if not FileCopy(ExistingConfigBackupPath, ConfigPath, False) then
    RaiseException('Failed to restore appsettings.json after cleaning the install directory.');
end;

procedure InitializeWizard();
begin
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
      Result := True;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  JsonPath: string;
  JsonContent: string;
begin
  if CurStep = ssInstall then
  begin
    BackupExistingConfig();
    CleanInstallDirectory();
  end;

  if CurStep = ssPostInstall then
  begin
    if HadExistingConfig then
    begin
      RestoreExistingConfig();
    end
    else if ShouldConfigure then
    begin
      if Assigned(ConfigPage) then
        SourceDir := ConfigPage.Values[0];

      JsonPath := ExpandConstant('{app}\appsettings.json');
      JsonContent := LoadTextFile(JsonPath);

      if JsonContent = '' then
        RaiseException('Installed appsettings.json was empty or missing.');

      JsonContent := ReplaceJsonStringValue(JsonContent, 'DefaultSourceDirectory', SourceDir);

      SaveStringToFile(JsonPath, JsonContent, False);
      MsgBox('appsettings.json created successfully.', mbInformation, MB_OK);
    end;
  end;
end;

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
