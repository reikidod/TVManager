[CmdletBinding()]
param(
    [switch]$SkipAdb,
    [switch]$OpenSamsungSetupPage,
    [switch]$OpenWebOsSetupPage
)

$ErrorActionPreference = 'Stop'
$toolsRoot = Join-Path $env:LOCALAPPDATA 'TVManager\tools'
$adbDirectory = Join-Path $toolsRoot 'android\platform-tools'
$adbPath = Join-Path $adbDirectory 'adb.exe'

function Test-CommandPath([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

function Find-Sdb {
    $candidatePaths = [System.Collections.Generic.List[string]]::new()
    if ($env:TIZEN_STUDIO) {
        $candidatePaths.Add((Join-Path $env:TIZEN_STUDIO 'tools\sdb.exe'))
        $candidatePaths.Add((Join-Path $env:TIZEN_STUDIO 'tools\ide\bin\sdb.exe'))
    }
    $candidatePaths.Add((Join-Path $env:LOCALAPPDATA 'TVManager\tools\samsung\sdb.exe'))
    $candidatePaths.Add('C:\tizen-studio\tools\sdb.exe')
    $candidatePaths.Add((Join-Path $env:ProgramFiles 'tizen-studio\tools\sdb.exe'))
    if (${env:ProgramFiles(x86)}) {
        $candidatePaths.Add((Join-Path ${env:ProgramFiles(x86)} 'tizen-studio\tools\sdb.exe'))
    }
    foreach ($candidate in $candidatePaths) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    return (Test-CommandPath 'sdb.exe')
}

Write-Host 'TV Manager — проверка внешних инструментов' -ForegroundColor Cyan
Write-Host "Каталог инструментов: $toolsRoot"

$adbFromPath = Test-CommandPath 'adb.exe'
if (Test-Path -LiteralPath $adbPath -PathType Leaf) {
    Write-Host "ADB уже установлен: $adbPath" -ForegroundColor Green
} elseif ($adbFromPath) {
    Write-Host "ADB уже доступен через PATH: $adbFromPath" -ForegroundColor Green
} elseif (-not $SkipAdb) {
    Write-Host ''
    Write-Host 'ADB нужен для расширенного обнаружения Android TV / Google TV / Amazon Fire TV.'
    Write-Host 'Пакет будет загружен напрямую с официального сервера Google.'
    Write-Host 'Перед загрузкой ознакомьтесь с условиями Android SDK:'
    Write-Host 'https://developer.android.com/studio/terms' -ForegroundColor Yellow
    $answer = Read-Host 'Вы принимаете условия Google и разрешаете загрузить Platform-Tools? Введите YES'
    if ($answer -ceq 'YES') {
        $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('TVManager-PlatformTools-' + [guid]::NewGuid().ToString('N'))
        $archivePath = Join-Path $tempRoot 'platform-tools.zip'
        $extractPath = Join-Path $tempRoot 'extracted'
        New-Item -ItemType Directory -Path $tempRoot, $extractPath -Force | Out-Null
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            $downloadUrl = 'https://dl.google.com/android/repository/platform-tools-latest-windows.zip'
            Write-Host 'Загрузка Android SDK Platform-Tools...' -ForegroundColor Cyan
            Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
            Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath -Force
            $downloadedAdb = Join-Path $extractPath 'platform-tools\adb.exe'
            if (-not (Test-Path -LiteralPath $downloadedAdb -PathType Leaf)) {
                throw 'В официальном архиве не найден adb.exe. Установка отменена.'
            }
            if (Test-Path -LiteralPath $adbDirectory) {
                Remove-Item -LiteralPath $adbDirectory -Recurse -Force
            }
            New-Item -ItemType Directory -Path (Split-Path $adbDirectory -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $extractPath 'platform-tools') -Destination (Split-Path $adbDirectory -Parent) -Recurse -Force
            & $adbPath version
            if ($LASTEXITCODE -ne 0) { throw 'ADB установлен, но не прошёл проверку запуска.' }
            Write-Host "ADB установлен: $adbPath" -ForegroundColor Green
        } finally {
            if (Test-Path -LiteralPath $tempRoot) {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    } else {
        Write-Host 'ADB не устанавливался: пользователь не подтвердил условия.' -ForegroundColor Yellow
    }
} else {
    Write-Host 'Установка ADB пропущена по параметру -SkipAdb.' -ForegroundColor Yellow
}

$sdb = Find-Sdb
if ($sdb) {
    Write-Host "SDB найден: $sdb" -ForegroundColor Green
} else {
    Write-Host 'SDB не найден. Для Samsung Tizen установите официальный Tizen Studio / TV SDK Tools:' -ForegroundColor Yellow
    Write-Host 'https://developer.samsung.com/smarttv/develop/tools/tizen-studio.html'
    if ($OpenSamsungSetupPage) { Start-Process 'https://developer.samsung.com/smarttv/develop/tools/tizen-studio.html' }
}

Write-Host ''
Write-Host 'LG webOS: сетевое обнаружение SSDP уже встроено в TV Manager.' -ForegroundColor Green
Write-Host 'Для будущих функций управления можно установить официальный webOS CLI:'
Write-Host 'https://webostv.developer.lge.com/develop/tools/cli-installation'
if ($OpenWebOsSetupPage) { Start-Process 'https://webostv.developer.lge.com/develop/tools/cli-installation' }

Write-Host ''
Write-Host 'Проверка завершена. Перезапустите TV Manager, затем выполните поиск устройств.' -ForegroundColor Cyan
