#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.1.0',
    [string]$OutputDirectory = 'artifacts'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot 'artifacts'
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
if ($outputPath -ne $artifactRoot -and !$outputPath.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release output must be under artifacts/ so personal settings cannot be replaced.'
}
$stagingPath = Join-Path $artifactRoot ('.staging/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingPath, $outputPath -Force | Out-Null

Push-Location $repoRoot
try {
    dotnet publish src/SoftwareDeviceSwitch.csproj -c Release -r win-x64 --self-contained false "-p:Version=$Version" -p:ContinuousIntegrationBuild=true -o $stagingPath
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'config/profile.example.json') -Destination (Join-Path $stagingPath 'profile.json')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/QUICKSTART.md') -Destination (Join-Path $stagingPath 'README.md')
    $starter = Get-Content -LiteralPath (Join-Path $stagingPath 'profile.json') -Raw | ConvertFrom-Json
    if (@($starter.Profiles.PSObject.Properties).Count -ne 0) { throw 'Release starter configuration must not contain personal profiles.' }

    $executable = Join-Path $outputPath 'SoftwareDeviceSwitch.exe'
    $archive = Join-Path $outputPath "SoftwareDeviceSwitch-$Version-win-x64.zip"
    Copy-Item -LiteralPath (Join-Path $stagingPath 'SoftwareDeviceSwitch.exe') -Destination $executable -Force
    $packageFiles = @('SoftwareDeviceSwitch.exe', 'profile.json', 'README.md') | ForEach-Object { Join-Path $stagingPath $_ }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipStream = [System.IO.File]::Open($archive, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
    try {
        $zip = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($file in $packageFiles) {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file, [System.IO.Path]::GetFileName($file), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $zipStream.Dispose() }
    $checksums = foreach ($file in @($executable, $archive)) {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($algorithm.ComputeHash([System.IO.File]::ReadAllBytes($file))).Replace('-', '').ToLowerInvariant() }
        finally { $algorithm.Dispose() }
        "$hash  $([System.IO.Path]::GetFileName($file))"
    }
    [System.IO.File]::WriteAllLines((Join-Path $outputPath 'SHA256SUMS.txt'), $checksums)
    Write-Host "Release $Version prepared in $outputPath"
}
finally { Pop-Location }
