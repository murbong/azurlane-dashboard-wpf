#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Subject = "CN=AzurLaneDashboard Internal Code Signing",
    [string]$FriendlyName = "AzurLaneDashboard Internal Code Signing",
    [ValidateRange(1, 10)]
    [int]$ValidYears = 3,
    [string]$PublicCertificatePath = "",
    [string]$PfxPath = "",
    [SecureString]$PfxPassword
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($PublicCertificatePath)) {
    $baseDirectory = if ($env:LOCALAPPDATA) {
        Join-Path $env:LOCALAPPDATA "AzurLaneDashboard\signing"
    }
    else {
        Join-Path $HOME ".azurlane-dashboard\signing"
    }

    $PublicCertificatePath = Join-Path $baseDirectory "azurlane-dashboard-internal-code-signing.cer"
}

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -FriendlyName $FriendlyName `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($ValidYears)

$publicCertificateDirectory = Split-Path -Parent $PublicCertificatePath
if ($publicCertificateDirectory -and -not (Test-Path -LiteralPath $publicCertificateDirectory)) {
    New-Item -ItemType Directory -Path $publicCertificateDirectory -Force | Out-Null
}

Export-Certificate -Cert $certificate -FilePath $PublicCertificatePath -Force | Out-Null

if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    if (-not $PfxPassword) {
        $PfxPassword = Read-Host "PFX password" -AsSecureString
    }

    $pfxDirectory = Split-Path -Parent $PfxPath
    if ($pfxDirectory -and -not (Test-Path -LiteralPath $pfxDirectory)) {
        New-Item -ItemType Directory -Path $pfxDirectory -Force | Out-Null
    }

    Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $PfxPassword -Force | Out-Null
}

Write-Host "Created internal code signing certificate."
Write-Host "Subject:    $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Public certificate: $PublicCertificatePath"

if (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    Write-Host "PFX export: $PfxPath"
}
