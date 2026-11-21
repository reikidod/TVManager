[CmdletBinding()]
param([switch]$NoArchive)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $projectRoot 'src\TVManager.App\TVManager.App.csproj'
$artifactRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot 'publish\win-x64'
$packageDirectory = Join-Path $artifactRoot 'package\TVManager'
$version = '0.1.2'

if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "Project file was not found: $projectFile"
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'The .NET SDK was not found. Install the .NET 10 SDK and reopen PowerShell.'
}

$sdkLines = @(& dotnet --list-sdks)
if ($LASTEXITCODE -ne 0) {
    throw "Could not query installed .NET SDKs. Exit code: $LASTEXITCODE"
}
$net10Sdk = $sdkLines | Where-Object { $_ -match '^10\.' } | Select-Object -First 1
if (-not $net10Sdk) {
    throw 'The .NET 10 SDK is required. Install it with: winget install --id Microsoft.DotNet.SDK.10 --exact'
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $packageDirectory) {
    Remove-Item -LiteralPath $packageDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Write-Host 'Publishing TV Manager for Windows x64...' -ForegroundColor Cyan
& dotnet publish $projectFile `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:PublishTrimmed=false `
    --output $publishDirectory

$publishExitCode = $LASTEXITCODE
if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode. No GitHub push was performed."
}

$exePath = Join-Path $publishDirectory 'TVManager.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
    throw "Publish completed but TVManager.exe was not found in $publishDirectory. No GitHub push was performed."
}

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory -Force
$notices = Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md'
if (Test-Path -LiteralPath $notices -PathType Leaf) {
    Copy-Item -LiteralPath $notices -Destination $packageDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools') -Destination $packageDirectory -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-DeviceTools.ps1') -Destination (Join-Path $packageDirectory 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-DeviceTools.cmd') -Destination (Join-Path $packageDirectory 'scripts') -Force

$startHere = @'
TV Manager 0.1.2 - Windows x64

1. Run TVManager.exe.
2. For Android TV / Google TV / Fire TV, run scripts\Install-DeviceTools.cmd if ADB is not installed.
3. For Samsung Tizen, install the official Tizen Studio / TV SDK Tools.
4. LG webOS discovery uses the built-in SSDP/UPnP implementation.

The .NET runtime is included in this self-contained application package.
External vendor tools are installed separately after reviewing and accepting their vendor terms.
'@
Set-Content -LiteralPath (Join-Path $packageDirectory 'START-HERE.txt') -Value $startHere -Encoding UTF8

Write-Host "Publish folder: $packageDirectory" -ForegroundColor Green
if (-not $NoArchive) {
    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    }
    $archive = Join-Path $artifactRoot "TVManager-$version-win-x64.zip"
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive -Force
    }
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw 'The ZIP archive was not created. No GitHub push was performed.'
    }
    $archiveInfo = Get-Item -LiteralPath $archive
    if ($archiveInfo.Length -lt 100000) {
        throw "The ZIP archive is unexpectedly small ($($archiveInfo.Length) bytes). No GitHub push was performed."
    }
    Write-Host "Package ready: $($archiveInfo.FullName) ($($archiveInfo.Length) bytes)" -ForegroundColor Green
}
