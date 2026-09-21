#ifndef AppVersion
  #define AppVersion "1.2.13"
#endif
#ifndef SourceDir
  #define SourceDir "..\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
#endif
#ifndef OutputDir
  #define OutputDir "dist"
#endif

[Setup]
AppId=Gerenciador ICP Brasil
AppName=Gerenciador ICP Brasil
AppVersion={#AppVersion}
AppPublisher=Rede ICP Brasil
DefaultDirName={autopf}\Gerenciador ICP Brasil
DefaultGroupName=Gerenciador ICP Brasil
DisableDirPage=yes
DisableWelcomePage=yes
DisableReadyPage=yes
DisableFinishedPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=GerenciadorICPBrasilSetup-{#AppVersion}
SetupIconFile=..\Assets\icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
UsePreviousPrivileges=no
CloseApplications=yes
RestartApplications=no
RestartIfNeededByRun=no
LicenseFile=consent.txt
UninstallDisplayIcon={app}\GerenciadorIcpBrasil.exe
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName=Gerenciador ICP Brasil
VersionInfoCompany=Rede ICP Brasil
VersionInfoDescription=Instalador do Gerenciador ICP Brasil

#define AppProtocolScheme "gerenciador-icp-brasil"
#define LegacyAppProtocolScheme "assistente-icp"

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WizardLicense=Acordo de Licença
ptbr.LicenseLabel=Leia atentamente as informações a seguir antes de continuar.
ptbr.LicenseLabel3=Você deve aceitar os termos do acordo para prosseguir com a instalação.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Gerenciador ICP Brasil"; Filename: "{app}\GerenciadorIcpBrasil.exe"
Name: "{commondesktop}\Gerenciador ICP Brasil"; Filename: "{app}\GerenciadorIcpBrasil.exe"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Gerenciador ICP Brasil"; Flags: deletevalue
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Assistente ICP"; Flags: deletevalue
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Gerenciador ICP Brasil"; ValueData: """{app}\GerenciadorIcpBrasil.exe"" --background-startup"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}"; ValueType: string; ValueName: ""; ValueData: "Gerenciador ICP Brasil protocol"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GerenciadorIcpBrasil.exe,0"
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GerenciadorIcpBrasil.exe"" ""%1"""

Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}"; ValueType: string; ValueName: ""; ValueData: "Gerenciador ICP Brasil protocol (compatibilidade)"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GerenciadorIcpBrasil.exe,0"
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GerenciadorIcpBrasil.exe"" ""%1"""

[Run]
Filename: "{app}\GerenciadorIcpBrasil.exe"; Description: "Abrir Gerenciador ICP Brasil"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  ProtectedWorkDirectory: string;

function EnsureProtectedWorkDirectory(): Boolean;
var
  AclExitCode: Integer;
  AclParams: string;
begin
  Result := False;
  if ProtectedWorkDirectory = '' then
  begin
    ProtectedWorkDirectory :=
      ExpandConstant('{commonappdata}\Gerenciador ICP Brasil\installer-cache\') +
      GetDateTimeString('yyyymmddhhnnsszzz', '-', ':');

    if not ForceDirectories(ProtectedWorkDirectory) then
    begin
      ProtectedWorkDirectory := '';
      exit;
    end;

    AclParams := '"' + ProtectedWorkDirectory + '" /inheritance:r /grant:r ' +
      '*S-1-5-18:(OI)(CI)F *S-1-5-32-544:(OI)(CI)F';
    if (not Exec(ExpandConstant('{sys}\icacls.exe'), AclParams, '', SW_HIDE,
      ewWaitUntilTerminated, AclExitCode)) or (AclExitCode <> 0) then
    begin
      DelTree(ProtectedWorkDirectory, True, True, True);
      ProtectedWorkDirectory := '';
      exit;
    end;
  end;

  Result := True;
end;

function WriteAuditScript(): string;
var
  ScriptPath: string;
  ScriptText: string;
begin
  if not EnsureProtectedWorkDirectory() then
  begin
    Result := '';
    exit;
  end;

  ScriptPath := ProtectedWorkDirectory + '\gerenciador_audit.ps1';
  ScriptText :=
    'param([string]$EventType,[string]$AppVersion)' + #13#10 +
    '$ErrorActionPreference = "SilentlyContinue"' + #13#10 +
    '$programData = [Environment]::GetFolderPath("CommonApplicationData")' + #13#10 +
    '$auditDir = Join-Path $programData "Gerenciador ICP Brasil\\audit"' + #13#10 +
    'New-Item -ItemType Directory -Force $auditDir | Out-Null' + #13#10 +
    '$installPath = Join-Path $auditDir "installation.json"' + #13#10 +
    '$installationId = $null' + #13#10 +
    'if (Test-Path $installPath) {' + #13#10 +
    '  try { $installationId = (Get-Content $installPath -Raw | ConvertFrom-Json).installationId } catch {}' + #13#10 +
    '}' + #13#10 +
    'if (-not $installationId) {' + #13#10 +
    '  $installationId = [guid]::NewGuid().ToString("N")' + #13#10 +
    '  @{installationId=$installationId} | ConvertTo-Json | Set-Content $installPath' + #13#10 +
    '}' + #13#10 +
    '$timestamp = (Get-Date).ToUniversalTime().ToString("o")' + #13#10 +
    '$event = @{ ' + #13#10 +
    '  installationId=$installationId; eventId=[guid]::NewGuid().ToString("N"); eventType=$EventType; timestamp=$timestamp;' + #13#10 +
    '  os=[System.Environment]::OSVersion.Platform.ToString();' + #13#10 +
    '  osVersion=[System.Environment]::OSVersion.VersionString; arch=[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();' + #13#10 +
    '  appVersion=$AppVersion; source="installer"' + #13#10 +
    '}' + #13#10 +
    '$line = $event | ConvertTo-Json -Compress' + #13#10 +
    'Add-Content (Join-Path $auditDir "events.jsonl") $line' + #13#10 +
    '$cutoff = (Get-Date).ToUniversalTime().AddMonths(-12)' + #13#10 +
    'if (Test-Path (Join-Path $auditDir "events.jsonl")) {' + #13#10 +
    '  $lines = Get-Content (Join-Path $auditDir "events.jsonl")' + #13#10 +
    '  $kept = @()' + #13#10 +
    '  foreach ($l in $lines) {' + #13#10 +
    '    if (-not [string]::IsNullOrWhiteSpace($l)) {' + #13#10 +
    '      try { $evt = $l | ConvertFrom-Json; if ([DateTime]$evt.timestamp -ge $cutoff) { $kept += $l } } catch {}' + #13#10 +
    '    }' + #13#10 +
    '  }' + #13#10 +
    '  Set-Content (Join-Path $auditDir "events.jsonl") $kept' + #13#10 +
    '}' + #13#10 +
    '' + #13#10;

  SaveStringToFile(ScriptPath, ScriptText, False);
  Result := ScriptPath;
end;

procedure RunAuditEvent(EventType: string);
var
  ScriptPath: string;
  Params: string;
  ResultCode: Integer;
begin
  ScriptPath := WriteAuditScript();
  if ScriptPath = '' then
  begin
    exit;
  end;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-EventType "' + EventType + '" ' +
    '-AppVersion "{#SetupSetting("AppVersion")}"';
  Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure DeinitializeSetup();
begin
  if ProtectedWorkDirectory <> '' then
  begin
    DelTree(ProtectedWorkDirectory, True, True, True);
  end;
end;

procedure DeinitializeUninstall();
begin
  if ProtectedWorkDirectory <> '' then
  begin
    DelTree(ProtectedWorkDirectory, True, True, True);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RunAuditEvent('app.install');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RunAuditEvent('app.uninstall');
  end;
end;
