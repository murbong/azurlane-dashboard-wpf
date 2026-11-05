#requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [switch]$LocalMachine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedCertificatePath = (Resolve-Path -LiteralPath $CertificatePath).Path
$certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($resolvedCertificatePath)

$storeLocation = if ($LocalMachine) { "LocalMachine" } else { "CurrentUser" }

if ($LocalMachine) {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdministrator) {
        throw "LocalMachine certificate installation requires an elevated PowerShell session."
    }
}

$stores = @(
    "Cert:\$storeLocation\Root",
    "Cert:\$storeLocation\TrustedPublisher"
)

foreach ($store in $stores) {
    if ($PSCmdlet.ShouldProcess($store, "Import $($certificate.Subject)")) {
        Import-Certificate -FilePath $resolvedCertificatePath -CertStoreLocation $store | Out-Null
    }
}

Write-Host "Installed internal signing certificate."
Write-Host "Subject:    $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Store scope: $storeLocation"
