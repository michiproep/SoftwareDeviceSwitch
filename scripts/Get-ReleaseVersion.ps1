#requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Tag)

$versionMatch = [regex]::Match($Tag, '^v(0|[1-9]\d*)\.(0|[1-9]\d*)(?:\.(0|[1-9]\d*))?$')
if (!$versionMatch.Success) { throw "Invalid release tag '$Tag'. Use vMAJOR.MINOR or vMAJOR.MINOR.PATCH." }
$patch = if ($versionMatch.Groups[3].Success) { $versionMatch.Groups[3].Value } else { '0' }
"$($versionMatch.Groups[1].Value).$($versionMatch.Groups[2].Value).$patch"
