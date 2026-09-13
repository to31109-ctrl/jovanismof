# Cuts a release: bumps the version, builds the small archive the in-game updater downloads,
# and rebuilds the full installer package with the update repository baked in.
#
#   .\distribution\Release.ps1 -Version 5.1.2 -UpdateRepository yourname/jovanismof
#
# Afterwards, upload the printed archive to a GitHub release tagged with that same version.
# Players pick it up on their next launch and it installs when they quit.
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$UpdateRepository,
    [string]$GamePath = 'D:\megabonk\Megabonk\BonkLink-Coop',
    [string]$BepInExZip = 'D:\megabonk\Megabonk\CoOpDev\tools\BepInEx-752.zip',
    [string]$OutputRoot = 'D:\megabonk\Megabonk\CoOpDev\artifacts'
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 5.1.2, got '$Version'." }
if ($UpdateRepository -notmatch '^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$') { throw "Repository must look like owner/repo, got '$UpdateRepository'." }

$repo = Split-Path $PSScriptRoot -Parent
$csproj = Join-Path $repo 'src/plugin/MegabonkTogether.Plugin.csproj'

# The version compiled into the assembly is what players compare against, so it has to be
# the same number the GitHub release is tagged with.
$text = Get-Content -Raw -LiteralPath $csproj
$updated = [regex]::Replace($text, '<Version>\d+\.\d+\.\d+</Version>', "<Version>$Version</Version>", 1)
if ($updated -ne $text) {
    Set-Content -LiteralPath $csproj -Value $updated -Encoding UTF8
    Write-Host "Version set to $Version."
} else {
    Write-Host "Version already $Version."
}

$updateDir = Join-Path $OutputRoot 'updates'
& (Join-Path $PSScriptRoot 'Build-UpdateZip.ps1') -GamePath $GamePath -OutputDirectory $updateDir -Name 'JOVANISMOF' | Out-Null

$archive = Join-Path $updateDir "JOVANISMOF-$Version.zip"
if (!(Test-Path -LiteralPath $archive)) { throw "Expected the update archive at $archive." }

# A fresh installer package too, so a brand new player also learns where updates come from.
$packageDir = Join-Path $OutputRoot "release-$Version"
if (Test-Path -LiteralPath $packageDir) { Remove-Item -LiteralPath $packageDir -Recurse -Force }
& (Join-Path $PSScriptRoot 'Build-Package.ps1') -GamePath $GamePath -BepInExZip $BepInExZip -OutputDirectory $packageDir -UpdateRepository $UpdateRepository | Out-Null

$stage = Join-Path $packageDir 'BonkLink-Playtest'
$root = (Resolve-Path $stage).Path
$lines = foreach ($f in (Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName)) {
    if ($f.Name -eq 'SHA256SUMS.txt') { continue }
    $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
    "$h  " + $f.FullName.Substring($root.Length + 1).Replace('\', '/')
}
$lines | Out-File -FilePath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8

$installer = Join-Path $OutputRoot "JOVANISMOF-$Version-installer.zip"
if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force }
Compress-Archive -Path $stage -DestinationPath $installer

Write-Host ''
Write-Host '================ release ready ================'
Write-Host "Update archive (attach to the GitHub release):"
Write-Host "  $archive"
Write-Host "Installer package (for someone with no copy yet):"
Write-Host "  $installer"
Write-Host ''
Write-Host "Now, on https://github.com/$UpdateRepository :"
Write-Host "  1. Push this source. GPL-2.0 requires the source for what you hand out."
Write-Host "  2. Releases -> Draft a new release."
Write-Host "  3. Tag it exactly: $Version"
Write-Host "  4. Attach JOVANISMOF-$Version.zip and publish."
Write-Host ''
Write-Host 'Everyone already on the mod picks it up next launch and it installs when they quit.'
