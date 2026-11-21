[CmdletBinding()]
param([switch]$NoArchive)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'src\TVManager.App\TVManager.App.csproj'
$artifactRoot = Join-Path $projectRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot 'publish\win-x64'
$packageDirectory = Join-Path $artifactRoot 'package\TVManager'
$version = '0.1.2'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Не найден .NET SDK. Для сборки исходников установите .NET 10 SDK.'
}
$sdkVersion = (& dotnet --version).Trim()
if (-not $sdkVersion.StartsWith('10.')) {
    throw "Требуется .NET 10 SDK, обнаружена версия $sdkVersion."
}

if (Test-Path -LiteralPath $publishDirectory) { Remove-Item -LiteralPath $publishDirectory -Recurse -Force }
if (Test-Path -LiteralPath $packageDirectory) { Remove-Item -LiteralPath $packageDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

Write-Host 'Публикация TV Manager для Windows x64...' -ForegroundColor Cyan
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:PublishTrimmed=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с кодом $LASTEXITCODE" }

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools') -Destination $packageDirectory -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $packageDirectory 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-DeviceTools.ps1') -Destination (Join-Path $packageDirectory 'scripts') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-DeviceTools.cmd') -Destination (Join-Path $packageDirectory 'scripts') -Force

@"
TV Manager $version — Windows x64

1. Запустите TVManager.exe.
2. Для Android TV / Google TV / Fire TV запустите scripts\\Install-DeviceTools.ps1,
   если ADB ещё не установлен.
3. Для Samsung Tizen установите официальный Tizen Studio / TV SDK Tools.
4. LG webOS обнаруживается через встроенный SSDP/UPnP.

.NET runtime уже включён в публикацию приложения.
Внешние инструменты устанавливаются отдельно после принятия соответствующих условий их поставщиков.
"@ | Set-Content -LiteralPath (Join-Path $packageDirectory 'START-HERE.txt') -Encoding UTF8

Write-Host "Папка публикации: $packageDirectory" -ForegroundColor Green
if (-not $NoArchive) {
    $archive = Join-Path $artifactRoot "TVManager-$version-win-x64.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archive -CompressionLevel Optimal
    Write-Host "Архив готов: $archive" -ForegroundColor Green
}
