# Installs over an EXISTING installation whose updater is switched off, which is the state
# every copy already in a player's hands is in.
#
# Reading the installer is not enough here. The repair rewrites a config file section by
# section, and the ways that goes wrong -- a setting written outside [Updates], a second copy
# of a setting, a lost PlayerIdentity -- all leave a file that still looks plausible. So this
# installs for real and reads the result back the way BepInEx would.
param(
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [Parameter(Mandatory = $true)][string]$GamePath
)
$ErrorActionPreference = 'Stop'

$expectedRepository = ''
$repositoryFile = Join-Path $StageDirectory 'update-repository.txt'
if (Test-Path -LiteralPath $repositoryFile) { $expectedRepository = (Get-Content -LiteralPath $repositoryFile -Raw).Trim() }
if (!$expectedRepository) {
    Write-Host 'No update repository is staged, so there is no existing-config repair to verify.'
    exit 0
}

$failures = @()
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok   $what" } else { Write-Host "  FAIL $what" -ForegroundColor Red; $script:failures += $what }
}

# Reads a key only where BepInEx would read it: inside the named section.
function Get-SectionValue([string]$text, [string]$section, [string]$key) {
    $m = [regex]::Match($text, '(?ms)^\[' + $section + '\][^\r\n]*\r?\n(?:(?!^\[).)*')
    if (!$m.Success) { return $null }
    $k = [regex]::Match($m.Value, '(?m)^' + $key + '\s*=[ \t]*([^\r\n]*)')
    if (!$k.Success) { return $null }
    return $k.Groups[1].Value.Trim()
}

# The first case is the shape BepInEx actually writes, comments and all, with the updater off
# and no repository -- what is on every machine the mod has already been sent to.
$realConfig = @'
## Settings file was created by plugin JOVANISMOF v5.1.1
## Plugin GUID: MegabonkTogether

[Player]

## Your display name shown to other players.
# Setting type: String
PlayerName = daneel

## Internal, stable id for this installation.
# Setting type: String
PlayerIdentity = e161eb0f01d64beba7719172465bd552

[Updates]

## Check for a newer build on launch.
# Setting type: Boolean
# Default value: true
CheckForUpdates = false

## Internal flag to show changelog after an update.
# Setting type: Boolean
ShowChangelog = false

## GitHub repository to update this mod from, as owner/repo.
# Setting type: String
UpdateRepository =
'@

$cases = [ordered]@{
    'a real config with the updater off and no repository' = $realConfig
    'no [Updates] section at all' = "[Player]`r`nPlayerName = friend`r`nPlayerIdentity = abc123`r`n"
    '[Updates] present but empty' = "[Player]`r`nPlayerName = friend`r`nPlayerIdentity = abc123`r`n`r`n[Updates]`r`n"
    '[Updates] before another section, already correct' = "[Updates]`r`nCheckForUpdates = true`r`nUpdateRepository = $expectedRepository`r`n`r`n[Player]`r`nPlayerName = friend`r`nPlayerIdentity = abc123`r`n"
}

# Installing creates shortcuts; none of them belong to this test, so put back what was there.
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Play JOVANISMOF.lnk'
$desktopExisted = Test-Path -LiteralPath $desktopLink
$desktopBackup = if ($desktopExisted) { [IO.File]::ReadAllBytes($desktopLink) } else { $null }
$stageLink = Join-Path $StageDirectory 'Play JOVANISMOF.lnk'
$stageBackup = if (Test-Path -LiteralPath $stageLink) { [IO.File]::ReadAllBytes($stageLink) } else { $null }

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-cfgtest-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    foreach ($name in $cases.Keys) {
        Write-Host ''
        Write-Host "Installing over: $name"
        if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
        New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'Megabonk_Data') | Out-Null
        Copy-Item -LiteralPath (Join-Path $GamePath 'Megabonk.exe') -Destination $scratch -Force
        'placeholder' | Set-Content -LiteralPath (Join-Path $scratch 'Megabonk_Data\verify.txt')

        # Seed an already-installed copy carrying this config, the way a player's machine looks.
        $installed = Join-Path $scratch 'BonkLink-Coop'
        $configRoot = Join-Path $installed 'BepInEx\config'
        New-Item -ItemType Directory -Force -Path $configRoot | Out-Null
        Set-Content -LiteralPath (Join-Path $installed '.bonklink-install') -Value 'BonkLink edition of Megabonk Together'
        $configFile = Join-Path $configRoot 'MegabonkTogether.cfg'
        $before = $cases[$name]
        [IO.File]::WriteAllText($configFile, $before, (New-Object System.Text.UTF8Encoding($false)))

        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $StageDirectory 'Install.ps1') -GamePath $scratch | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "The installer failed over an existing installation (exit $LASTEXITCODE)." }

        $after = Get-Content -Raw -LiteralPath $configFile
        Check ((Get-SectionValue $after 'Updates' 'CheckForUpdates') -eq 'true') 'CheckForUpdates = true, inside [Updates]'
        Check ((Get-SectionValue $after 'Updates' 'UpdateRepository') -eq $expectedRepository) "UpdateRepository = $expectedRepository, inside [Updates]"

        # A setting written twice makes BepInEx keep only one of them, which is how a repair
        # quietly undoes itself on the next launch.
        foreach ($key in @('CheckForUpdates', 'UpdateRepository')) {
            $count = ([regex]::Matches($after, '(?m)^' + $key + '\s*=')).Count
            Check ($count -eq 1) "$key appears exactly once (found $count)"
        }
        Check ($after -notmatch '(?m)^\[Updates\][\s\S]*^\[Updates\]') '[Updates] is not duplicated'

        # Losing this makes a host stop recognising the player and hand them a new character.
        if ($before -match 'PlayerIdentity\s*=\s*(\S+)') {
            Check ((Get-SectionValue $after 'Player' 'PlayerIdentity') -eq $Matches[1]) "PlayerIdentity preserved ($($Matches[1]))"
        }
        foreach ($section in [regex]::Matches($before, '(?m)^\[([^\]]+)\]')) {
            $s = $section.Groups[1].Value
            Check ($after -match ('(?m)^\[' + [regex]::Escape($s) + '\]')) "section [$s] survived"
        }
    }
}
finally {
    if ($desktopExisted) { [IO.File]::WriteAllBytes($desktopLink, $desktopBackup) }
    elseif (Test-Path -LiteralPath $desktopLink) { Remove-Item -LiteralPath $desktopLink -Force -ErrorAction SilentlyContinue }
    if ($null -ne $stageBackup) { [IO.File]::WriteAllBytes($stageLink, $stageBackup) }
    elseif (Test-Path -LiteralPath $stageLink) { Remove-Item -LiteralPath $stageLink -Force -ErrorAction SilentlyContinue }
    $full = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notmatch '^jovanismof-cfgtest-[a-f0-9]{8}$') {
        throw "Refusing cleanup outside the verification scratch directory: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "Existing-config installation failed ($($failures.Count)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    throw 'Installing over an existing copy would not switch the updater on correctly.'
}
Write-Host 'Existing-config installation verified: the updater is switched on without losing anything.'
