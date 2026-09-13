# Builds the small archive that the in-game updater downloads.
#
# This is NOT the installer package. It contains only the plugin's own files, flat, because
# the updater extracts every entry directly into BepInEx/plugins/MegabonkTogether. Players
# who already have the mod get this; a new player still needs the full installer package.
param(
    [Parameter(Mandatory = $true)][string]$GamePath,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Name = 'BonkLink'
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Push-Location $repo
try {
    dotnet build src/plugin/MegabonkTogether.Plugin.csproj -c Release -p:CI=true -p:DefineConstants=TRACE "-p:MegabonkPath=$GamePath" --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $built = Join-Path $repo 'src/plugin/bin/Release/net6.0'

    # The version players compare against is the one compiled into the assembly, so read it
    # from the built file rather than trusting anything written by hand.
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $built 'MegabonkTogether.dll')).FileVersion
    $version = ($version -split '\.')[0..2] -join '.'
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Could not read a three-part version, got '$version'." }

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $staging = Join-Path $OutputDirectory "update-$version"
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null

    # Flat, because the updater extracts entries by name with no directories.
    Get-ChildItem -LiteralPath $built -File |
        Where-Object Extension -in @('.dll', '.json', '.toml') |
        Copy-Item -Destination $staging

    $zip = Join-Path $OutputDirectory "$Name-$version.zip"
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Write-Host ''
    Write-Host "Update archive: $zip"
    Write-Host "Version:        $version"
    Write-Host "SHA-256:        $hash"
    Write-Host ''
    Write-Host 'Publish it as a GitHub release on the repository named by UpdateRepository:'
    Write-Host "  1. Bump <Version> in src/plugin/MegabonkTogether.Plugin.csproj before building."
    Write-Host "  2. Push the matching source. GPL-2.0 requires the source for what you distribute."
    Write-Host "  3. Create a release tagged $version and attach $Name-$version.zip to it."
    Write-Host ''
    Write-Host 'Players pick it up on their next launch and it applies when they quit.'
}
finally {
    Pop-Location
}
