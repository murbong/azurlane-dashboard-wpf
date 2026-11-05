param(
    [string]$Profile = "win-x64-publish",
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

$root = Split-Path -Parent $PSScriptRoot
$projectDir = Join-Path $root "src\AzurLaneDashboard.App"
$project = Join-Path $projectDir "AzurLaneDashboard.App.csproj"
$profilePath = Join-Path $projectDir "Properties\PublishProfiles\$Profile.pubxml"

if (-not (Test-Path $profilePath)) {
    throw "Publish profile not found: $profilePath"
}

[xml]$profileXml = Get-Content -LiteralPath $profilePath
$configuration = [string]$profileXml.Project.PropertyGroup.Configuration
$targetFramework = [string]$profileXml.Project.PropertyGroup.TargetFramework
$runtimeIdentifier = [string]$profileXml.Project.PropertyGroup.RuntimeIdentifier
$selfContained = [string]$profileXml.Project.PropertyGroup.SelfContained
$publishDirValue = [string]$profileXml.Project.PropertyGroup.PublishDir
if ([string]::IsNullOrWhiteSpace($publishDirValue)) {
    throw "Publish profile does not define PublishDir: $profilePath"
}
if ([string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
    throw "Publish profile does not define RuntimeIdentifier: $profilePath"
}
if ([System.IO.Path]::IsPathRooted($publishDirValue)) {
    $output = $publishDirValue
} else {
    $output = Join-Path $projectDir $publishDirValue
}
$output = [System.IO.Path]::GetFullPath($output)
$distRoot = [System.IO.Path]::GetFullPath((Join-Path $root "dist"))

if (-not $output.StartsWith($distRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to publish outside dist: $output"
}

if ($Clean -and (Test-Path $output)) {
    $resolved = (Resolve-Path $output).Path
    if (-not $resolved.StartsWith($distRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean unexpected path: $resolved"
    }

    $runningFromOutput = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($resolved, [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($runningFromOutput.Count -gt 0) {
        $processList = ($runningFromOutput | ForEach-Object { "$($_.ProcessName)($($_.Id))" }) -join ", "
        throw "Cannot clean publish output while processes are running from it: $processList"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

function Invoke-DotnetRestore {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -eq 0) {
        return
    }

    $firstExitCode = $LASTEXITCODE
    $globalPackages = if (-not [string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) {
        $env:NUGET_PACKAGES
    } else {
        Join-Path $env:USERPROFILE ".nuget\packages"
    }

    if (-not (Test-Path $globalPackages)) {
        throw "dotnet restore failed with exit code $firstExitCode"
    }

    Write-Warning "dotnet restore failed with exit code $firstExitCode. Retrying with local NuGet cache: $globalPackages"
    & dotnet @Arguments --source $globalPackages --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed with exit code $LASTEXITCODE"
    }
}

$restoreArgs = @("restore", $project, "-p:PublishProfile=$Profile")
if (-not [string]::IsNullOrWhiteSpace($configuration)) {
    $restoreArgs += "-p:Configuration=$configuration"
}
if (-not [string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
    $restoreArgs += "-p:RuntimeIdentifiers=$runtimeIdentifier"
}
if (-not [string]::IsNullOrWhiteSpace($selfContained)) {
    $restoreArgs += "-p:SelfContained=$selfContained"
}

Invoke-DotnetRestore -Arguments $restoreArgs

$publishArgs = @("publish", $project, "-p:PublishProfile=$Profile", "--no-restore")
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$defaultMapsDir = Join-Path $output "defaults\maps"
$mutableMapsDir = Join-Path $output "maps"
if (Test-Path $defaultMapsDir) {
    New-Item -ItemType Directory -Force -Path $mutableMapsDir | Out-Null
    foreach ($source in Get-ChildItem -LiteralPath $defaultMapsDir -File -Filter "*.json") {
        $target = Join-Path $mutableMapsDir $source.Name
        $shouldCopy = -not (Test-Path $target)
        if (-not $shouldCopy) {
            $targetItem = Get-Item -LiteralPath $target
            $shouldCopy = $source.LastWriteTimeUtc -gt $targetItem.LastWriteTimeUtc
        }

        if ($shouldCopy) {
            Copy-Item -LiteralPath $source.FullName -Destination $target -Force
        }
    }
}

$required = @(
    "AzurLaneDashboard.App.exe",
    "AzurLaneDashboard.Resources.dll",
    "AssetsTools.NET.dll",
    "AssetsTools.NET.Texture.dll",
    "AssetRipper.TextureDecoder.dll",
    "StbImageWriteSharp.dll"
)

foreach ($name in $required) {
    $path = Join-Path $output $name
    if (-not (Test-Path $path)) {
        throw "Missing published file: $name"
    }
}

$pythonFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File -Filter "*.py" -ErrorAction SilentlyContinue)
$mutableMapFiles = @(Get-ChildItem -LiteralPath (Join-Path $output "maps") -File -Filter "*.json" -ErrorAction SilentlyContinue)
$defaultMapFiles = @(Get-ChildItem -LiteralPath (Join-Path $output "defaults\maps") -File -Filter "*.json" -ErrorAction SilentlyContinue)
$allFiles = @(Get-ChildItem -LiteralPath $output -Recurse -File)
$sizeMb = [Math]::Round((($allFiles | Measure-Object Length -Sum).Sum / 1MB), 2)

Write-Host "WPF publish complete"
Write-Host "  Profile: $Profile"
Write-Host "  Output : $output"
Write-Host "  Files  : $($allFiles.Count)"
Write-Host "  SizeMB : $sizeMb"
Write-Host "  UserMaps: $($mutableMapFiles.Count)"
Write-Host "  Defaults: $($defaultMapFiles.Count)"
Write-Host "  Python : $($pythonFiles.Count)"

if ($defaultMapFiles.Count -eq 0) {
    throw "Published output does not contain defaults maps"
}

if ($pythonFiles.Count -ne 0) {
    throw "Published output contains Python files"
}

if ($Sign) {
    $signScript = Join-Path $PSScriptRoot "sign_wpf_output.ps1"
    if (-not (Test-Path -LiteralPath $signScript)) {
        throw "Signing script not found: $signScript"
    }

    $signArgs = @{
        Path = $output
        CertificateSubject = $SignCertificateSubject
        ExecutableNamePrefix = $SignExecutableNamePrefix
        AssemblyNamePrefix = $SignAssemblyNamePrefix
    }

    if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
        $signArgs.Thumbprint = $SignThumbprint
    }

    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $signArgs.TimestampUrl = $TimestampUrl
    }

    if ($NoTimestamp) {
        $signArgs.NoTimestamp = $true
    }

    if ($VerifySignature) {
        $signArgs.Verify = $true
    }

    if ($IncludeAllExe) {
        $signArgs.IncludeAllExe = $true
    }

    if ($IncludeAllDll) {
        $signArgs.IncludeAllDll = $true
    }

    & $signScript @signArgs
}

if ($Zip) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $zipPath = Join-Path $distRoot "AzurLaneDashboard-$Profile-$stamp.zip"
    Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $zipItem = Get-Item -LiteralPath $zipPath
    Write-Host "  Zip    : $($zipItem.FullName)"
    Write-Host "  ZipMB  : $([Math]::Round($zipItem.Length / 1MB, 2))"
}
