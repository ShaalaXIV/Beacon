[CmdletBinding()]
param(
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'verify-release.ps1') -AllowDirty:$AllowDirty

    [xml]$project = Get-Content -LiteralPath 'src/Compass.Plugin/Compass.Plugin.csproj'
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $versionNode) {
        throw 'Compass.Plugin.csproj does not declare a Version.'
    }
    $assemblyVersion = $versionNode.InnerText
    if ($assemblyVersion -notmatch '^(\d+\.\d+\.\d+)\.\d+$') {
        throw "Plugin version '$assemblyVersion' is not a four-part release version."
    }

    $releaseVersion = $Matches[1]
    $publishDirectory = Join-Path $repoRoot 'publish'
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

    $destination = Join-Path $publishDirectory "Compass-$releaseVersion.zip"
    Copy-Item -LiteralPath 'src/Compass.Plugin/bin/Release/Compass/latest.zip' -Destination $destination -Force

    $hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256
    Set-Content -LiteralPath "$destination.sha256" -Value "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($destination))" -Encoding utf8NoBOM

    Write-Host "Release package: $destination"
    Write-Host "SHA-256: $($hash.Hash.ToLowerInvariant())"
}
finally {
    Pop-Location
}
