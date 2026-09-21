[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'artifacts/app-unsigned'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot $OutputDirectory
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutputPath = [System.IO.Path]::GetFullPath($outputPath)

if (-not $resolvedOutputPath.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase))
{
    throw 'O diretório de saída deve estar dentro de artifacts/.'
}

if (Test-Path -LiteralPath $resolvedOutputPath)
{
    Remove-Item -LiteralPath $resolvedOutputPath -Recurse -Force
}

dotnet restore (Join-Path $repositoryRoot 'GerenciadorICPBrasil.sln') --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar as dependências da solução.' }

dotnet publish (Join-Path $repositoryRoot 'GerenciadorIcpBrasil.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:WindowsAppSDKSelfContained=true `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -p:PublishDir="$outputPath\"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o aplicativo principal.' }

dotnet publish (Join-Path $repositoryRoot 'Helpers\ElevatedConfiguration\ConfigAuditoria.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -p:PublishDir="$outputPath\Helpers\ElevatedConfiguration\"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o executor administrativo.' }

dotnet publish (Join-Path $repositoryRoot 'Helpers\CertificateSelector\CertSelector.csproj') `
    -c Release `
    -r $Runtime `
    --self-contained true `
    --no-restore `
    -p:Version=$Version `
    -p:AssemblyVersion="$Version.0" `
    -p:FileVersion="$Version.0" `
    -p:PublishDir="$outputPath\Helpers\CertificateSelector\"
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o seletor de certificados.' }

$manifestPath = Join-Path $outputPath 'app-version.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.version = $Version
$manifest.publishedAt = [DateTime]::UtcNow.ToString('o')
$manifest.sha256 = ''
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Produto unificado não assinado gerado em $outputPath"
