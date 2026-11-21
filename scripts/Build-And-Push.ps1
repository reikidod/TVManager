[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solution = Join-Path $projectRoot 'TVManager.sln'
$project = Join-Path $projectRoot 'src\TVManager.App\TVManager.App.csproj'
$publisher = Join-Path $PSScriptRoot 'Publish-Windows.ps1'
$githubPublisher = Join-Path $PSScriptRoot 'Publish-To-GitHub.ps1'
$artifact = Join-Path $projectRoot 'artifacts\TVManager-0.1.2-win-x64.zip'

function Assert-NativeSuccess {
    param([Parameter(Mandatory = $true)][string]$Operation)
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE. No GitHub push was performed."
    }
}

foreach ($file in @($solution, $project, $publisher, $githubPublisher)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required project file was not found: $file"
    }
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $dotnetExe -PathType Leaf) {
        $env:Path = "$(Split-Path -Parent $dotnetExe);$env:Path"
    } else {
        throw 'The .NET SDK is missing. Install it with: winget install Microsoft.DotNet.SDK.10. Then reopen PowerShell.'
    }
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    $ghExe = Join-Path $env:ProgramFiles 'GitHub CLI\gh.exe'
    if (Test-Path -LiteralPath $ghExe -PathType Leaf) {
        $env:Path = "$(Split-Path -Parent $ghExe);$env:Path"
    } else {
        throw 'GitHub CLI was not found. Install it with: winget install --id GitHub.cli --exact.'
    }
}

$sdkLines = @(& dotnet --list-sdks)
Assert-NativeSuccess -Operation '.NET SDK detection'
$hasNet10 = $sdkLines | Where-Object { $_ -match '^10\.' }
if (-not $hasNet10) {
    throw 'The .NET 10 SDK is required. Install it with: winget install Microsoft.DotNet.SDK.10. Then reopen PowerShell.'
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git for Windows was not found. Install Git before publishing.'
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI was not found. Install GitHub CLI and reopen PowerShell.'
}

Push-Location $projectRoot
try {
    Write-Host ''
    Write-Host '=== 1/4 RESTORE ===' -ForegroundColor Cyan
    & dotnet restore $solution
    Assert-NativeSuccess -Operation 'dotnet restore'

    Write-Host ''
    Write-Host '=== 2/4 BUILD RELEASE ===' -ForegroundColor Cyan
    & dotnet build $solution --configuration Release --no-restore
    Assert-NativeSuccess -Operation 'dotnet build'

    Write-Host ''
    Write-Host '=== 3/4 PUBLISH SELF-CONTAINED WINDOWS X64 ===' -ForegroundColor Cyan
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $publisher
    Assert-NativeSuccess -Operation 'Windows publish'

    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Publish completed but package was not found: $artifact. No GitHub push was performed."
    }
    $package = Get-Item -LiteralPath $artifact
    if ($package.Length -lt 100000) {
        throw "The package size looks unexpectedly small ($($package.Length) bytes). No GitHub push was performed."
    }
    Write-Host "Package verified: $($package.FullName) ($($package.Length) bytes)" -ForegroundColor Green

    Write-Host ''
    Write-Host '=== 4/4 PUSH SOURCE TO GITHUB ===' -ForegroundColor Cyan
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $githubPublisher
    Assert-NativeSuccess -Operation 'GitHub publishing'

    Write-Host ''
    Write-Host 'Build, package and GitHub push completed.' -ForegroundColor Green
    Write-Host 'GitHub Actions will run the independent Windows build after the push.' -ForegroundColor Green
}
finally {
    Pop-Location
}
