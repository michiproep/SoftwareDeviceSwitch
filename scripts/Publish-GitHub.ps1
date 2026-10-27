#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$RepositoryName = 'SoftwareDeviceSwitch',
    [ValidatePattern('^[A-Za-z0-9-]*$')][string]$Owner = '',
    [string]$Tag = 'v0.1'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Tag $Tag
function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & git @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($Arguments -join ' ')" }
}
function Push-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & git -c credential.helper= -c 'credential.https://github.com.helper=!gh auth git-credential' push @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'git push failed.' }
}

Push-Location $repoRoot
try {
    $login = gh api user --jq .login
    if ($LASTEXITCODE -ne 0) { throw 'GitHub CLI cannot reach an authenticated account. Check network access and run gh auth login if needed.' }
    if (!$Owner) { $Owner = $login.Trim() }
    $repository = "$Owner/$RepositoryName"
    $expectedUrl = "https://github.com/$repository"
    $gitRoot = Invoke-Git rev-parse --show-toplevel
    if ([System.IO.Path]::GetFullPath($gitRoot) -ne $repoRoot) { throw 'Run this script from its own Git repository.' }
    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $origin = git remote get-url origin 2>$null
        $originStatus = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousErrorPreference }
    if ($originStatus -eq 0) {
        $normalized = ($origin.Trim().Replace('git@github.com:', 'https://github.com/').TrimEnd('/') -replace '\.git$', '')
        if ($normalized -ne $expectedUrl) { throw "origin points to another repository: $normalized" }
        $visibility = gh repo view $repository --json visibility --jq .visibility
        if ($LASTEXITCODE -ne 0 -or $visibility -ne 'PUBLIC') { throw 'The existing origin must be the intended public repository.' }
    }
    else { $origin = $null }

    # Verify local Git writes before any remote repository is created.
    Invoke-Git add --dry-run -- .gitignore README.md SoftwareDeviceSwitch.slnx src tests scripts config docs .github
    dotnet build SoftwareDeviceSwitch.slnx -c Release --warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --project tests/SoftwareDeviceSwitch.Checks -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Checks failed.' }
    & (Join-Path $PSScriptRoot 'Build-Release.ps1') -Version $version -OutputDirectory artifacts/release

    # Remove the personal file from the index, retaining the actual local file.
    Invoke-Git rm --cached --ignore-unmatch -- publish/profile.json
    Invoke-Git add -A -- .gitignore README.md SoftwareDeviceSwitch.slnx src tests scripts config docs .github
    Invoke-Git add -u -- '*.cs' '*.csproj'
    git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) { Invoke-Git commit -m "Prepare $Tag public release and GitHub Actions" }
    elseif ($LASTEXITCODE -ne 0) { throw 'Could not inspect staged changes.' }
    $branch = Invoke-Git branch --show-current
    if ($branch -ne 'main') { Invoke-Git branch -m main }
    $head = Invoke-Git rev-parse HEAD
    git show-ref --verify --quiet "refs/tags/$Tag"
    if ($LASTEXITCODE -eq 0) {
        $tagCommit = Invoke-Git rev-parse "$Tag^{commit}"
        if ($tagCommit -ne $head) { throw "$Tag already points at another commit. Choose a new release tag." }
    }
    else { Invoke-Git tag -a $Tag -m "Device Switch $Tag" }

    if (!$origin) {
        gh repo create $repository --public --source . --remote origin --description 'Windows monitor and Logitech device switching with profiles, global shortcuts and a system tray.'
        if ($LASTEXITCODE -ne 0) { throw 'Public repository creation failed. No push was performed.' }
    }
    Push-Git -u origin main
    gh repo edit $repository --default-branch main
    if ($LASTEXITCODE -ne 0) { throw 'Could not select main as the default branch.' }
    Push-Git origin "refs/tags/$Tag"
    Write-Host "Repository: $expectedUrl"
    Write-Host "The Release workflow will build and publish $Tag with executable, ZIP and checksums."
    Write-Host "Watch: $expectedUrl/actions/workflows/release.yml"
}
finally { Pop-Location }
