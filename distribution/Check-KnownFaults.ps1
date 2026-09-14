# Refuses to let a package be built if a fault that already reached players comes back.
#
# Everything checked here is something that compiled cleanly and broke in a player's hands.
# The compiler cannot catch these: IL2CPP simply does not have some of the Unity overloads
# that exist in normal .NET, so the call builds and throws the moment it runs.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$faults = @()

function Get-PluginSource {
    Get-ChildItem -LiteralPath (Join-Path $repo 'src') -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.Name -ne 'Helper.cs' }
}

$source = Get-PluginSource

# 1. IL2CPP has no GameObject(string, params Type[]). This is what stopped the Host button.
foreach ($file in $source) {
    $n = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $n++
        if ($line -match 'new\s+GameObject\s*\([^)]*typeof') {
            $faults += "$($file.Name):$n uses new GameObject(name, typeof(...)), which throws under IL2CPP. Construct it, then AddComponent."
        }
    }
}

# 2. IL2CPP has no generic GetComponents<T>() / GetComponentsInChildren<T>(bool).
#    These are what stopped the pause menu save button appearing.
foreach ($file in $source) {
    $n = 0
    foreach ($line in Get-Content -LiteralPath $file.FullName) {
        $n++
        if ($line -match '(?<!Runtime)\.GetComponentsInChildren<' -and $line -notmatch 'RuntimeGetComponentsInChildren') {
            $faults += "$($file.Name):$n calls GetComponentsInChildren<T>(), which throws under IL2CPP. Use RuntimeGetComponentsInChildren<T>()."
        }
        if ($line -match '(?<!Runtime)\.GetComponents<' -and $line -notmatch 'RuntimeGetComponents') {
            $faults += "$($file.Name):$n calls GetComponents<T>(), which throws under IL2CPP. Use RuntimeGetComponents<T>()."
        }
    }
}

# 3. The launcher reads the game path as plain text, so a byte-order mark becomes part of
# Native Window wrappers must resolve the CharacterMenu component, not use a managed as cast.
foreach ($file in $source) {
    if ((Get-Content -Raw -LiteralPath $file.FullName) -match '\bas\s+CharacterMenu\b') {
        $faults += "$($file.Name) casts a native Window with as CharacterMenu; use its CharacterMenu component."
    }
}

# The launcher reads the game path as plain text, so a byte-order mark becomes part of
#    the path and nothing can be found there. This is what broke Play JOVANISMOF.
$installer = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1')
if ($installer -match "gamepath\.txt'\)\s*-Value[^\r\n]*-Encoding\s+UTF8") {
    $faults += 'Install.ps1 writes gamepath.txt with -Encoding UTF8, which adds a byte-order mark. Use UTF8Encoding($false).'
}
if ($installer -notmatch 'UTF8Encoding\(\$false\)') {
    $faults += 'Install.ps1 no longer writes gamepath.txt without a byte-order mark.'
}

$packager = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Build-Package.ps1')
if ($packager -match "update-repository\.txt'\)\s*-Value.*-Encoding\s+UTF8") {
    $faults += 'Build-Package.ps1 writes update-repository.txt with -Encoding UTF8, which adds a byte-order mark. The updater would then reject the repository name.'
}

$launcher = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Play.vbs')
if ($launcher -notmatch 'StripMark') {
    $faults += 'Play.vbs no longer strips a byte-order mark from the game path.'
}

# 4. The shortcut has to appear where the player just ran the installer.
if ($installer -notmatch '\$PSScriptRoot,\s*\$target') {
    $faults += 'Install.ps1 no longer creates the shortcut in the extracted folder.'
}

# 5. A test build must never be shipped: it redirects saves and quits on a timer.
foreach ($file in $source) {
    if ($file.Name -eq 'BonkLinkSmoke.cs') { continue }
    if ((Get-Content -Raw -LiteralPath $file.FullName) -match '#define\s+BONKLINK_TESTING') {
        $faults += "$($file.Name) defines BONKLINK_TESTING in source."
    }
}

if ($faults.Count -gt 0) {
    Write-Host ''
    Write-Host 'Refusing to package. Faults that already reached players have come back:' -ForegroundColor Red
    foreach ($f in $faults) { Write-Host "  - $f" -ForegroundColor Red }
    Write-Host ''
    throw 'Known-fault check failed.'
}

Write-Host "Known-fault check passed ($($source.Count) source files)."
