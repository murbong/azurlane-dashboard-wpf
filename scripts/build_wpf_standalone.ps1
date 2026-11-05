param(
    [switch]$Clean,
    [switch]$Zip,
    [switch]$Sign,
    [string]$SignThumbprint = "",
    [string]$SignCertificateSubject = "AzurLaneDashboard Internal Code Signing",
    [string]$SignExecutableNamePrefix = "AzurLaneDashboard",
    [string]$SignAssemblyNamePrefix = "AzurLaneDashboard",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$NoTimestamp,
    [switch]$VerifySignature,
    [switch]$IncludeAllExe,
    [switch]$IncludeAllDll
)

$ErrorActionPreference = "Stop"

$script = Join-Path $PSScriptRoot "publish_wpf.ps1"
& $script `
    -Profile "win-x64-standalone" `
    -Clean:$Clean `
    -Zip:$Zip `
    -Sign:$Sign `
    -SignThumbprint $SignThumbprint `
    -SignCertificateSubject $SignCertificateSubject `
    -SignExecutableNamePrefix $SignExecutableNamePrefix `
    -SignAssemblyNamePrefix $SignAssemblyNamePrefix `
    -TimestampUrl $TimestampUrl `
    -NoTimestamp:$NoTimestamp `
    -VerifySignature:$VerifySignature `
    -IncludeAllExe:$IncludeAllExe `
    -IncludeAllDll:$IncludeAllDll
