#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [string]$PackageDirectory = 'artifacts/release'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = & (Join-Path $PSScriptRoot 'Get-ReleaseVersion.ps1') -Tag $Tag
$packagePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PackageDirectory))
$assets = @('SoftwareDeviceSwitch.exe', "SoftwareDeviceSwitch-$version-win-x64.zip", 'SHA256SUMS.txt') | ForEach-Object { Join-Path $packagePath $_ }
foreach ($asset in $assets) { if (!(Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Missing release asset: $asset" } }

$previousErrorPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    $existing = gh release view $Tag --repo $Repository --json isDraft 2>$null
    $existingStatus = $LASTEXITCODE
}
finally { $ErrorActionPreference = $previousErrorPreference }
if ($existingStatus -eq 0) {
    if (!(($existing | ConvertFrom-Json).isDraft)) { throw "Release $Tag is already public. Use a new tag instead of replacing published assets." }
    gh release upload $Tag @assets --repo $Repository --clobber
    if ($LASTEXITCODE -ne 0) { throw 'Uploading draft release assets failed.' }
}
else {
    $notesFile = Join-Path $repoRoot "docs/releases/$Tag.md"
    $createArgs = @('release', 'create', $Tag) + $assets + @('--repo', $Repository, '--verify-tag', '--draft', '--title', "Device Switch $Tag")
    if (Test-Path -LiteralPath $notesFile) { $createArgs += @('--notes-file', $notesFile) }
    else { $createArgs += '--generate-notes' }
    gh @createArgs
    if ($LASTEXITCODE -ne 0) { throw 'Creating the draft release failed.' }
}
gh release edit $Tag --repo $Repository --draft=false --latest
if ($LASTEXITCODE -ne 0) { throw 'Publishing the release failed; the uploaded draft can be retried.' }
