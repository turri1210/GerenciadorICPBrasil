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

#define WebView2BootstrapperUrl "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
#define WindowsAppRuntimeInstallerUrl "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe"
#define DotNetDesktopRuntimeInstallerUrl "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
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
  PrereqPage: TOutputProgressWizardPage;
  PrereqRequestedRestart: Boolean;
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
  PrereqPage.SetText(StatusText, Format('Concluído %d de %d.', [Completed, Total]));
end;

function WritePrereqCheckScript(const CheckId: string): string;
var
  ScriptPath: string;
  ScriptText: string;
begin
  if not EnsureProtectedWorkDirectory() then
  begin
    Result := '';
    exit;
  end;

  ScriptPath := ProtectedWorkDirectory + '\gerenciador_prereq_check.ps1';

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
  if ScriptPath = '' then
  begin
    exit;
  end;
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
  if not EnsureProtectedWorkDirectory() then
  begin
    Result := '';
    exit;
  end;

  ScriptPath := ProtectedWorkDirectory + '\gerenciador_prereq_install.ps1';
  ScriptText :=
    'param([string]$Url,[string]$FileName,[string]$CacheDirectory)' + #13#10 +
    '$ErrorActionPreference = "Stop"' + #13#10 +
    '$target = Join-Path $CacheDirectory $FileName' + #13#10 +
    'Invoke-WebRequest -Uri $Url -OutFile $target -UseBasicParsing' + #13#10 +
    '$lock = [System.IO.File]::Open($target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)' + #13#10 +
    'try {' + #13#10 +
    '  $signature = Get-AuthenticodeSignature -FilePath $target' + #13#10 +
    '  $publisher = if ($signature.SignerCertificate) { $signature.SignerCertificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false) } else { "" }' + #13#10 +
    '  if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or $publisher -ne "Microsoft Corporation") {' + #13#10 +
    '    throw "O complemento baixado não possui assinatura válida da Microsoft Corporation."' + #13#10 +
    '  }' + #13#10 +
    '  $args = ' + InstallArgsLiteral + #13#10 +
    '  $p = Start-Process -FilePath $target -ArgumentList $args -Wait -PassThru -WindowStyle Hidden' + #13#10 +
    '  exit $p.ExitCode' + #13#10 +
    '} finally {' + #13#10 +
    '  $lock.Dispose()' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(ScriptPath, ScriptText, False);
  Result := ScriptPath;
end;

function InstallPrerequisite(const Url: string; const FileName: string; const InstallArgsLiteral: string; var InstallExitCode: Integer): Boolean;
var
  ScriptPath: string;
  Params: string;
begin
  ScriptPath := WritePrereqInstallScript(InstallArgsLiteral);
  if ScriptPath = '' then
  begin
    InstallExitCode := -1;
    Result := False;
    exit;
  end;
  Params := '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '" ' +
    '-Url "' + Url + '" ' +
    '-FileName "' + FileName + '" ' +
    '-CacheDirectory "' + ProtectedWorkDirectory + '"';
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
    UpdatePrereqProgress(CompletedBefore + 1, TotalCount, DisplayName + ' já está instalado.');
    Result := '';
    exit;
  end;

  UpdatePrereqProgress(CompletedBefore, TotalCount, 'Instalando ' + DisplayName + '...');
  if not InstallPrerequisite(DownloadUrl, DownloadFileName, InstallArgsLiteral, InstallExitCode) then
  begin
    Result := 'Não foi possível iniciar a instalação de ' + DisplayName + '.';
    exit;
  end;

  if (InstallExitCode <> 0) and (InstallExitCode <> 3010) and (InstallExitCode <> 1641) then
  begin
    Result := DisplayName + ' retornou código de saída ' + IntToStr(InstallExitCode) + '.';
    exit;
  end;

  if (InstallExitCode = 3010) or (InstallExitCode = 1641) then
  begin
    PrereqRequestedRestart := True;
  end;

  if not WaitForPrerequisiteInstalled(CheckId, 12, 5000) then
  begin
    Result := 'A instalação de ' + DisplayName + ' foi executada, mas não foi possível confirmar o componente.';
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
    PrereqPage.SetText('Iniciando verificação de complementos...', 'Concluído 0 de 3.');
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
  Result := EnsurePrerequisitesInstalled(NeedsRestart);
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
