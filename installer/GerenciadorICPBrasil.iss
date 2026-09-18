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
AppName=Assistente ICP
AppVersion={#AppVersion}
DefaultDirName={autopf}\Gerenciador ICP Brasil
DefaultGroupName=Assistente ICP
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
VersionInfoProductName=Assistente ICP
VersionInfoCompany=Rede ICP Brasil
VersionInfoDescription=Instalador do Assistente ICP

#define WebView2BootstrapperUrl "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
#define WindowsAppRuntimeInstallerUrl "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe"
#define DotNetDesktopRuntimeInstallerUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
#define AppProtocolScheme "assistente-icp"
#define LegacyAppProtocolScheme "gerenciador-icp-brasil"

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Messages]
ptbr.WizardLicense=Acordo de Licenca
ptbr.LicenseLabel=Leia atentamente as informacoes a seguir antes de continuar.
ptbr.LicenseLabel3=Voce deve aceitar os termos do acordo para prosseguir com a instalacao.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\Assistente ICP"; Filename: "{app}\GerenciadorIcpBrasil.exe"
Name: "{commondesktop}\Assistente ICP"; Filename: "{app}\GerenciadorIcpBrasil.exe"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Gerenciador ICP Brasil"; Flags: deletevalue
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Assistente ICP"; ValueData: """{app}\GerenciadorIcpBrasil.exe"" --background-startup"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}"; ValueType: string; ValueName: ""; ValueData: "Assistente ICP protocol"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GerenciadorIcpBrasil.exe,0"
Root: HKLM; Subkey: "Software\Classes\{#AppProtocolScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GerenciadorIcpBrasil.exe"" ""%1"""

Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}"; ValueType: string; ValueName: ""; ValueData: "Assistente ICP protocol (legado)"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\GerenciadorIcpBrasil.exe,0"
Root: HKLM; Subkey: "Software\Classes\{#LegacyAppProtocolScheme}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\GerenciadorIcpBrasil.exe"" ""%1"""

[Run]
Filename: "{app}\GerenciadorIcpBrasil.exe"; Description: "Abrir Assistente ICP"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  PrereqPage: TOutputProgressWizardPage;
  PrereqRequestedRestart: Boolean;

procedure InitializeWizard();
begin
  PrereqPage := CreateOutputProgressPage(
    'Preparando a instalação',
    'Verificando e instalando complementos obrigatórios...');
end;

procedure UpdatePrereqProgress(Completed, Total: Integer; const StatusText: string);
begin
  if PrereqPage = nil then
  begin
    exit;
  end;

  PrereqPage.SetProgress(Completed, Total);
  PrereqPage.SetText(StatusText, Format('Concluido %d de %d.', [Completed, Total]));
end;

function WritePrereqCheckScript(const CheckId: string): string;
var
  ScriptPath: string;
  ScriptText: string;
begin
  ScriptPath := ExpandConstant('{tmp}\gerenciador_prereq_check.ps1');

  if CompareText(CheckId, 'webview2') = 0 then
  begin
    ScriptText :=
      '$keys = @(' + #13#10 +
      '  "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}",' + #13#10 +
      '  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"' + #13#10 +
      ')' + #13#10 +
      'foreach ($key in $keys) {' + #13#10 +
      '  try {' + #13#10 +
      '    $pv = (Get-ItemProperty -Path $key -Name "pv" -ErrorAction Stop).pv' + #13#10 +
      '    if (-not [string]::IsNullOrWhiteSpace($pv)) { exit 0 }' + #13#10 +
      '  } catch {}' + #13#10 +
      '}' + #13#10 +
      '$wvDir = Join-Path ${env:ProgramFiles(x86)} "Microsoft\EdgeWebView\Application"' + #13#10 +
      'if (Test-Path $wvDir) {' + #13#10 +
      '  $exe = Get-ChildItem -Path $wvDir -Filter "msedgewebview2.exe" -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1' + #13#10 +
      '  if ($exe) { exit 0 }' + #13#10 +
      '}' + #13#10 +
      'exit 1' + #13#10;
  end
  else if CompareText(CheckId, 'windowsappruntime') = 0 then
  begin
    ScriptText :=
      'function Test-VersionAtLeast {' + #13#10 +
      '  param([string]$Value, [int]$RequiredMajor, [int]$RequiredMinor)' + #13#10 +
      '  if ([string]::IsNullOrWhiteSpace($Value)) { return $false }' + #13#10 +
      '  $m = [regex]::Match($Value, "(\d+)\.(\d+)")' + #13#10 +
      '  if (-not $m.Success) { return $false }' + #13#10 +
      '  $major = [int]$m.Groups[1].Value' + #13#10 +
      '  $minor = [int]$m.Groups[2].Value' + #13#10 +
      '  if ($major -gt $RequiredMajor) { return $true }' + #13#10 +
      '  if ($major -lt $RequiredMajor) { return $false }' + #13#10 +
      '  return $minor -ge $RequiredMinor' + #13#10 +
      '}' + #13#10 +
      '$paths = @(' + #13#10 +
      '  "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",' + #13#10 +
      '  "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"' + #13#10 +
      ')' + #13#10 +
      'foreach ($path in $paths) {' + #13#10 +
      '  try {' + #13#10 +
      '    $matches = Get-ItemProperty -Path $path -ErrorAction SilentlyContinue |' + #13#10 +
      '      Where-Object { $_.DisplayName -like "Windows App Runtime*" }' + #13#10 +
      '    foreach ($match in $matches) {' + #13#10 +
      '      if (Test-VersionAtLeast $match.DisplayVersion 1 8) { exit 0 }' + #13#10 +
      '    }' + #13#10 +
      '  } catch {}' + #13#10 +
      '}' + #13#10 +
      'try {' + #13#10 +
      '  $appx = Get-AppxPackage -AllUsers -Name "Microsoft.WindowsAppRuntime*" -ErrorAction SilentlyContinue |' + #13#10 +
      '    Where-Object { $_.Name -match "^Microsoft\.WindowsAppRuntime\.(1\.[8-9]|[2-9]\.)" } | Select-Object -First 1' + #13#10 +
      '  if ($appx) { exit 0 }' + #13#10 +
      '} catch {}' + #13#10 +
      'exit 1' + #13#10;
  end
  else if CompareText(CheckId, 'dotnetdesktop') = 0 then
  begin
    ScriptText :=
      '$desktopDir = Join-Path ${env:ProgramFiles} "dotnet\shared\Microsoft.WindowsDesktop.App"' + #13#10 +
      'if (Test-Path $desktopDir) {' + #13#10 +
      '  $hit = Get-ChildItem -Path $desktopDir -Directory -ErrorAction SilentlyContinue |' + #13#10 +
      '    Where-Object { $_.Name -like "8.*" } | Select-Object -First 1' + #13#10 +
      '  if ($hit) { exit 0 }' + #13#10 +
      '}' + #13#10 +
      '$reg = "HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"' + #13#10 +
      'if (Test-Path $reg) {' + #13#10 +
      '  try {' + #13#10 +
      '    $props = Get-ItemProperty -Path $reg -ErrorAction Stop' + #13#10 +
      '    foreach ($p in $props.PSObject.Properties) {' + #13#10 +
      '      if ($p.Name -like "8.*" -and $p.Value -eq 1) { exit 0 }' + #13#10 +
      '    }' + #13#10 +
      '  } catch {}' + #13#10 +
      '}' + #13#10 +
      'exit 1' + #13#10;
  end
  else
  begin
    ScriptText := 'exit 1' + #13#10;
  end;

  SaveStringToFile(ScriptPath, ScriptText, False);
  Result := ScriptPath;
end;

function IsPrerequisiteInstalled(const CheckId: string): Boolean;
var
  ScriptPath: string;
  Params: string;
  ResultCode: Integer;
begin
  Result := False;
  ScriptPath := WritePrereqCheckScript(CheckId);
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"';
  if not Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    exit;
  end;
  Result := ResultCode = 0;
end;

function WritePrereqInstallScript(const InstallArgsLiteral: string): string;
var
  ScriptPath: string;
  ScriptText: string;
begin
  ScriptPath := ExpandConstant('{tmp}\gerenciador_prereq_install.ps1');
  ScriptText :=
    'param([string]$Url,[string]$FileName)' + #13#10 +
    '$ErrorActionPreference = "Stop"' + #13#10 +
    '$target = Join-Path $env:TEMP $FileName' + #13#10 +
    'Invoke-WebRequest -Uri $Url -OutFile $target -UseBasicParsing' + #13#10 +
    '$args = ' + InstallArgsLiteral + #13#10 +
    '$p = Start-Process -FilePath $target -ArgumentList $args -Wait -PassThru -WindowStyle Hidden' + #13#10 +
    'exit $p.ExitCode' + #13#10;
  SaveStringToFile(ScriptPath, ScriptText, False);
  Result := ScriptPath;
end;

function InstallPrerequisite(const Url: string; const FileName: string; const InstallArgsLiteral: string; var InstallExitCode: Integer): Boolean;
var
  ScriptPath: string;
  Params: string;
begin
  ScriptPath := WritePrereqInstallScript(InstallArgsLiteral);
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-Url "' + Url + '" ' +
    '-FileName "' + FileName + '"';
  Result := Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, InstallExitCode);
end;

function WaitForPrerequisiteInstalled(const CheckId: string; Retries: Integer; DelayMs: Integer): Boolean;
var
  Attempt: Integer;
begin
  for Attempt := 0 to Retries - 1 do
  begin
    if IsPrerequisiteInstalled(CheckId) then
    begin
      Result := True;
      exit;
    end;

    if Attempt < (Retries - 1) then
    begin
      Sleep(DelayMs);
    end;
  end;

  Result := False;
end;

function EnsureSinglePrerequisite(
  const CheckId: string;
  const DisplayName: string;
  const DownloadUrl: string;
  const DownloadFileName: string;
  const InstallArgsLiteral: string;
  CompletedBefore: Integer;
  TotalCount: Integer): string;
var
  InstallExitCode: Integer;
begin
  UpdatePrereqProgress(CompletedBefore, TotalCount, 'Verificando ' + DisplayName + '...');
  if IsPrerequisiteInstalled(CheckId) then
  begin
    UpdatePrereqProgress(CompletedBefore + 1, TotalCount, DisplayName + ' ja esta instalado.');
    Result := '';
    exit;
  end;

  UpdatePrereqProgress(CompletedBefore, TotalCount, 'Instalando ' + DisplayName + '...');
  if not InstallPrerequisite(DownloadUrl, DownloadFileName, InstallArgsLiteral, InstallExitCode) then
  begin
    Result := 'Nao foi possivel iniciar a instalacao de ' + DisplayName + '.';
    exit;
  end;

  if (InstallExitCode <> 0) and (InstallExitCode <> 3010) and (InstallExitCode <> 1641) then
  begin
    Result := DisplayName + ' retornou codigo de saida ' + IntToStr(InstallExitCode) + '.';
    exit;
  end;

  if (InstallExitCode = 3010) or (InstallExitCode = 1641) then
  begin
    PrereqRequestedRestart := True;
  end;

  if not WaitForPrerequisiteInstalled(CheckId, 12, 5000) then
  begin
    Result := 'A instalacao de ' + DisplayName + ' foi executada, mas nao foi possivel confirmar o componente.';
    exit;
  end;

  UpdatePrereqProgress(CompletedBefore + 1, TotalCount, DisplayName + ' instalado com sucesso.');
  Result := '';
end;

function EnsurePrerequisitesInstalled(var NeedsRestart: Boolean): string;
var
  TotalPrereq: Integer;
begin
  TotalPrereq := 3;
  Result := '';
  PrereqRequestedRestart := False;
  NeedsRestart := False;

  if PrereqPage <> nil then
  begin
    PrereqPage.SetProgress(0, TotalPrereq);
    PrereqPage.SetText('Iniciando verificacao de complementos...', 'Concluido 0 de 3.');
    PrereqPage.Show();
  end;

  try
    Result := EnsureSinglePrerequisite(
      'webview2',
      'Microsoft Edge WebView2 Runtime',
      '{#WebView2BootstrapperUrl}',
      'MicrosoftEdgeWebView2Setup.exe',
      '@("/silent","/install")',
      0,
      TotalPrereq);
    if Result <> '' then
    begin
      exit;
    end;

    Result := EnsureSinglePrerequisite(
      'windowsappruntime',
      'Windows App Runtime',
      '{#WindowsAppRuntimeInstallerUrl}',
      'WindowsAppRuntimeInstall-x64.exe',
      '@("--quiet","--force")',
      1,
      TotalPrereq);
    if Result <> '' then
    begin
      exit;
    end;

    Result := EnsureSinglePrerequisite(
      'dotnetdesktop',
      '.NET Desktop Runtime 8 x64',
      '{#DotNetDesktopRuntimeInstallerUrl}',
      'windowsdesktop-runtime-win-x64.exe',
      '@("/install","/quiet","/norestart")',
      2,
      TotalPrereq);
    if Result <> '' then
    begin
      exit;
    end;

    UpdatePrereqProgress(TotalPrereq, TotalPrereq, 'Complementos obrigatórios verificados.');
    NeedsRestart := PrereqRequestedRestart;
  finally
    if PrereqPage <> nil then
    begin
      PrereqPage.Hide();
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  NeedsRestart := False;
  Result := '';
end;

function WriteAuditScript(): string;
var
  ScriptPath: string;
  ScriptText: string;
begin
  ScriptPath := ExpandConstant('{tmp}\gerenciador_audit.ps1');
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
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-EventType "' + EventType + '" ' +
    '-AppVersion "{#SetupSetting("AppVersion")}"';
  Exec('powershell.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
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
