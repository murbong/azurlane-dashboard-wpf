#requires -Version 5.1
[CmdletBinding()]
param(
    [string[]]$Path = @(".\dist", ".\artifacts"),
    [string]$Thumbprint = "",
    [string]$CertificateSubject = "AzurLaneDashboard Internal Code Signing",
    [string]$ExecutableNamePrefix = "AzurLaneDashboard",
    [string]$AssemblyNamePrefix = "AzurLaneDashboard",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$NoTimestamp,
    [switch]$IncludeAllExe,
    [switch]$IncludeAllDll,
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $windowsKitsRoots = @()
    $programFilesX86 = ${env:ProgramFiles(x86)}

    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $windowsKitsRoots += Join-Path $programFilesX86 "Windows Kits\10\bin"
    }

    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $windowsKitsRoots += Join-Path $env:ProgramFiles "Windows Kits\10\bin"
    }

    foreach ($windowsKitsRoot in $windowsKitsRoots) {
        if (-not (Test-Path -LiteralPath $windowsKitsRoot)) {
            continue
        }

        $tools = @(Get-ChildItem -Path $windowsKitsRoot -Recurse -Filter "signtool.exe" -ErrorAction SilentlyContinue)
        $x64Tool = $tools |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($x64Tool) {
            return $x64Tool.FullName
        }

        $anyTool = $tools | Sort-Object FullName -Descending | Select-Object -First 1
        if ($anyTool) {
            return $anyTool.FullName
        }
    }

    throw "signtool.exe was not found. Install Windows SDK or add signtool.exe to PATH."
}

function Get-CodeSigningCertificate {
    param(
        [string]$RequestedThumbprint,
        [string]$RequestedSubject
    )

    $certificates = @(Get-ChildItem "Cert:\CurrentUser\My" | Where-Object { $_.NotAfter -gt (Get-Date) })

    if (-not [string]::IsNullOrWhiteSpace($RequestedThumbprint)) {
        $normalizedThumbprint = $RequestedThumbprint -replace "\s", ""
        $certificate = $certificates |
            Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
            Select-Object -First 1

        if (-not $certificate) {
            throw "No valid certificate found in Cert:\CurrentUser\My for thumbprint $RequestedThumbprint."
        }

        return $certificate
    }

    $candidates = $certificates |
        Where-Object {
            $hasCodeSigningEku = @(
                $_.EnhancedKeyUsageList |
                    Where-Object {
                        $_.ObjectId -eq "1.3.6.1.5.5.7.3.3" -or
                        $_.FriendlyName -eq "Code Signing"
                    }
            ).Count -gt 0

            $matchesSubject = (
                $_.Subject -like "*$RequestedSubject*" -or
                $_.FriendlyName -like "*$RequestedSubject*"
            )

            $hasCodeSigningEku -and $matchesSubject
        } |
        Sort-Object NotAfter -Descending

    $selectedCertificate = $candidates | Select-Object -First 1
    if (-not $selectedCertificate) {
        throw "No valid code signing certificate found for subject '$RequestedSubject' in Cert:\CurrentUser\My."
    }

    return $selectedCertificate
}

function Test-SignableFile {
    param([System.IO.FileInfo]$File)

    if ($File.Extension -eq ".exe") {
        if ($IncludeAllExe) {
            return $true
        }

        return $File.BaseName -like "$ExecutableNamePrefix*"
    }

    if ($File.Extension -ne ".dll") {
        return $false
    }

    if ($IncludeAllDll) {
        return $true
    }

    return $File.BaseName -like "$AssemblyNamePrefix*"
}

function Get-SignableFiles {
    param([string[]]$InputPaths)

    $files = New-Object System.Collections.Generic.List[string]

    foreach ($inputPath in $InputPaths) {
        $resolvedPaths = @(Resolve-Path -LiteralPath $inputPath -ErrorAction SilentlyContinue)
        if ($resolvedPaths.Count -eq 0) {
            Write-Warning "Path not found: $inputPath"
            continue
        }

        foreach ($resolvedPath in $resolvedPaths) {
            $item = Get-Item -LiteralPath $resolvedPath.Path

            if ($item.PSIsContainer) {
                Get-ChildItem -LiteralPath $item.FullName -Recurse -File |
                    Where-Object { Test-SignableFile -File $_ } |
                    ForEach-Object { $files.Add($_.FullName) }
            }
            elseif (Test-SignableFile -File $item) {
                $files.Add($item.FullName)
            }
        }
    }

    return @($files | Sort-Object -Unique)
}

$signTool = Find-SignTool
$certificate = Get-CodeSigningCertificate -RequestedThumbprint $Thumbprint -RequestedSubject $CertificateSubject
$targets = @(Get-SignableFiles -InputPaths $Path)

if ($targets.Count -eq 0) {
    throw "No .exe or .dll files were found to sign. Pass the publish output directory with -Path."
}

Write-Host "Using certificate: $($certificate.Subject)"
Write-Host "Thumbprint:        $($certificate.Thumbprint)"
Write-Host "SignTool:          $signTool"
Write-Host "Files to sign:     $($targets.Count)"
Write-Host "EXE filter:        $(if ($IncludeAllExe) { 'all EXEs' } else { "$ExecutableNamePrefix*.exe" })"
Write-Host "DLL filter:        $(if ($IncludeAllDll) { 'all DLLs' } else { "$AssemblyNamePrefix*.dll" })"

foreach ($target in $targets) {
    $signArguments = @(
        "sign",
        "/fd", "SHA256",
        "/sha1", $certificate.Thumbprint,
        "/s", "My"
    )

    if (-not $NoTimestamp -and -not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArguments += @("/tr", $TimestampUrl, "/td", "SHA256")
    }

    $signArguments += $target

    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Signing failed for $target."
    }

    if ($Verify) {
        & $signTool verify /pa /v $target
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed for $target."
        }
    }

    $signature = Get-AuthenticodeSignature -FilePath $target
    Write-Host "Signed: $target [$($signature.Status)]"
}
