[CmdletBinding()]
param(
    [switch]$Private
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))

# PowerShell 7.3+ can turn a native process' non-zero exit code into an error
# record. GitHub's "repository not found" response is expected during the
# first publish, so native exit codes are handled explicitly below.
$nativePreferenceExists = Test-Path -LiteralPath 'Variable:PSNativeCommandUseErrorActionPreference'
if ($nativePreferenceExists) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$GitArguments
    )

    & git -C $projectRoot @GitArguments
    $gitExitCode = $LASTEXITCODE
    if ($gitExitCode -ne 0) {
        throw "git $($GitArguments -join ' ') failed with exit code $gitExitCode."
    }
}

function Test-GitHubRepository {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $previousErrorActionPreference = $ErrorActionPreference
    try {
        # A missing repository is normal on the first run. Capture stderr and
        # inspect the process exit code instead of letting it abort the script.
        $ErrorActionPreference = 'Continue'
        $result = @(& gh repo view $Repository --json name 2>&1)
        $ghExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($ghExitCode -eq 0) {
        return $true
    }

    $details = ($result | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
    if ($details -match '(?i)not found|could not resolve to a repository|HTTP 404') {
        return $false
    }

    throw "Could not check GitHub repository '$Repository'. gh returned exit code $ghExitCode. $details"
}

foreach ($commandName in @('git', 'gh')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' was not found. Install Git for Windows and GitHub CLI."
    }
}

& gh auth status
if ($LASTEXITCODE -ne 0) {
    throw 'GitHub CLI is not authenticated. Run: gh auth login'
}

$githubLogin = (& gh api user --jq .login | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($githubLogin)) {
    throw 'Could not read the authenticated GitHub account.'
}

$repoFullName = "$githubLogin/TVManager"
$repoUrl = "https://github.com/$repoFullName.git"
$visibilityArgument = if ($Private) { '--private' } else { '--public' }

Write-Host "Target repository: $repoFullName" -ForegroundColor Cyan
if ($Private) {
    Write-Host 'Visibility: Private. Friends must be invited as collaborators.'
} else {
    Write-Host 'Visibility: Public. Friends can view and clone the repository.'
}

if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
    Invoke-Git -GitArguments @('init')
}

Invoke-Git -GitArguments @('branch', '-M', 'main')
Invoke-Git -GitArguments @('add', '-A')

# `git status --porcelain=v2 --branch` succeeds for an unborn branch and
# reports `# branch.oid (initial)`. This avoids calling rev-parse HEAD,
# which prints a fatal message when the repository has no first commit.
$statusLines = @(& git -C $projectRoot status --porcelain=v2 --branch)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the local Git repository status.'
}
$branchOidLine = $statusLines | Where-Object { $_ -like '# branch.oid *' } | Select-Object -First 1
$hasInitialCommit = ($null -ne $branchOidLine) -and ($branchOidLine -notmatch '\(initial\)$')

$stagedFiles = @(& git -C $projectRoot diff --cached --name-only)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect staged Git changes.'
}
$hasStagedChanges = $stagedFiles.Count -gt 0

if (-not $hasInitialCommit -or $hasStagedChanges) {
    $commitMessage = if ($hasInitialCommit) {
        'Update TV Manager source and packaging setup'
    } else {
        'Initial TV Manager source and packaging setup'
    }
    & git -C $projectRoot -c 'user.name=TV Manager Publisher' -c 'user.email=tv-manager-publisher@users.noreply.github.com' commit -m $commitMessage
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not create the Git commit.'
    }
} else {
    Write-Host 'No staged changes; keeping the existing commit.' -ForegroundColor Yellow
}

$remoteNames = @(& git -C $projectRoot remote)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect configured Git remotes.'
}

if ($remoteNames -contains 'origin') {
    $originUrl = (& git -C $projectRoot remote get-url origin | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the origin remote URL.'
    }
    $allowedOriginUrls = @(
        $repoUrl,
        "https://github.com/$repoFullName",
        "git@github.com:$repoFullName.git"
    )
    if ($originUrl -notin $allowedOriginUrls) {
        throw "The existing origin remote points somewhere else: $originUrl. Refusing to push to an unexpected repository."
    }
}

$repoExists = Test-GitHubRepository -Repository $repoFullName
if ($repoExists) {
    Write-Host "The GitHub repository $repoFullName already exists." -ForegroundColor Yellow
    $confirmation = Read-Host 'Type PUSH to push the current main branch to this repository'
    if ($confirmation -cne 'PUSH') {
        Write-Host 'Operation cancelled. No push was performed.'
        exit 0
    }
} else {
    Write-Host "A new repository will be created: $repoFullName" -ForegroundColor Cyan
    $confirmation = Read-Host 'Type YES to continue'
    if ($confirmation -cne 'YES') {
        Write-Host 'Operation cancelled. No repository was created.'
        exit 0
    }

    & gh repo create $repoFullName $visibilityArgument --description 'Windows Smart TV discovery and management utility'
    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub repository creation failed. Check the repository name, permissions, and network connection.'
    }
}

if (-not ($remoteNames -contains 'origin')) {
    Invoke-Git -GitArguments @('remote', 'add', 'origin', $repoUrl)
}

Invoke-Git -GitArguments @('push', '-u', 'origin', 'main')
Write-Host "Published successfully: https://github.com/$repoFullName" -ForegroundColor Green
