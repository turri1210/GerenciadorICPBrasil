[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$ArtifactDirectory,

    [switch]$RequireSignature
)

$ErrorActionPreference = 'Stop'
$resolvedArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path
$forbidden = Get-ChildItem -LiteralPath $resolvedArtifactDirectory -Recurse -File -Include *.pfx,*.p12,*.pem,*.key
if ($forbidden)
{
    throw "Material criptográfico proibido encontrado: $($forbidden.FullName -join ', ')"
}

$proprietary = Get-ChildItem -LiteralPath $resolvedArtifactDirectory -Recurse -File | Where-Object {
    $_.Name -in @('FTRAPI.dll', 'ftrScanAPI.dll', 'ftrSDKHelper13.dll') -or
    $_.FullName -match '[\\/]vendor[\\/]'
}
if ($proprietary)
{
    throw "Componente proprietário proibido encontrado: $($proprietary.FullName -join ', ')"
}

$ownedBinaries = @(
    Join-Path $resolvedArtifactDirectory 'GerenciadorIcpBrasil.exe'
    Join-Path $resolvedArtifactDirectory 'GerenciadorIcpBrasil.dll'
    Join-Path $resolvedArtifactDirectory 'Helpers\ElevatedConfiguration\ConfigAuditoria.exe'
    Join-Path $resolvedArtifactDirectory 'Helpers\ElevatedConfiguration\ConfigAuditoria.dll'
    Join-Path $resolvedArtifactDirectory 'Helpers\CertificateSelector\CertSelector.exe'
)

$requiredWinUiResources = @(
    Join-Path $resolvedArtifactDirectory 'GerenciadorIcpBrasil.pri'
    Join-Path $resolvedArtifactDirectory 'App.xbf'
    Join-Path $resolvedArtifactDirectory 'Views\MainPage.xbf'
)

$requiredSelfContainedFiles = @(
    Join-Path $resolvedArtifactDirectory 'Microsoft.UI.Xaml.dll'
    Join-Path $resolvedArtifactDirectory 'Microsoft.WindowsAppRuntime.dll'
    Join-Path $resolvedArtifactDirectory 'coreclr.dll'
    Join-Path $resolvedArtifactDirectory 'hostfxr.dll'
    Join-Path $resolvedArtifactDirectory 'Helpers\ElevatedConfiguration\coreclr.dll'
    Join-Path $resolvedArtifactDirectory 'Helpers\ElevatedConfiguration\hostfxr.dll'
)

foreach ($resource in $requiredWinUiResources)
{
    if (-not (Test-Path -LiteralPath $resource -PathType Leaf))
    {
        throw "Recurso WinUI obrigatório não encontrado: $resource"
    }
}

foreach ($dependency in $requiredSelfContainedFiles)
{
    if (-not (Test-Path -LiteralPath $dependency -PathType Leaf))
    {
        throw "Dependência self-contained obrigatória não encontrada: $dependency"
    }
}

foreach ($binary in $ownedBinaries)
{
    if (-not (Test-Path -LiteralPath $binary))
    {
        throw "Binário obrigatório não encontrado: $binary"
    }

    $versionInfo = (Get-Item -LiteralPath $binary).VersionInfo
    if ($versionInfo.ProductName -ne 'Gerenciador ICP Brasil')
    {
        throw "ProductName inesperado em ${binary}: $($versionInfo.ProductName)"
    }
    if (-not $versionInfo.ProductVersion.StartsWith($Version, [System.StringComparison]::Ordinal))
    {
        throw "ProductVersion inesperado em ${binary}: $($versionInfo.ProductVersion)"
    }

    if ($RequireSignature)
    {
        $signature = Get-AuthenticodeSignature -LiteralPath $binary
        if ($signature.Status -ne 'Valid')
        {
            throw "Assinatura inválida ou ausente em ${binary}: $($signature.Status)"
        }
    }
}

Write-Host 'Artefato validado com sucesso.'
