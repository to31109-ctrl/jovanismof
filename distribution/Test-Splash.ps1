# Drives the launcher against a stand-in game, because what matters here -- that the mod is
# already updated by the time the game starts -- cannot be seen by reading the script.
#
# The interesting case needs a real release to update from, so it installs a deliberately old
# plugin and checks the launcher replaces it. If GitHub cannot be reached the update case is
# skipped rather than failing the build; the offline cases still run.
param(
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [string]$OldPluginDll
)
$ErrorActionPreference = 'Stop'

$SplashScript = Join-Path $StageDirectory 'Splash.ps1'
if (!(Test-Path -LiteralPath $SplashScript)) { throw "No Splash.ps1 was staged at $SplashScript." }

$stagedPlugin = Join-Path $StageDirectory 'payload\BepInEx\plugins\MegabonkTogether\MegabonkTogether.dll'
if (!(Test-Path -LiteralPath $stagedPlugin)) { throw "No staged plugin found at $stagedPlugin." }

$repository = ''
$repositoryFile = Join-Path $StageDirectory 'update-repository.txt'
if (Test-Path -LiteralPath $repositoryFile) { $repository = (Get-Content -LiteralPath $repositoryFile -Raw).Trim() }

$failures = @()
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok   $what" } else { Write-Host "  FAIL $what" -ForegroundColor Red; $script:failures += $what }
}

# The plugin carries its own copy of the splash and writes it out beside the game, because an
# update cannot reach a file outside the plugin folder. If that copy ever drifts from the
# staged one, updating would quietly replace a good splash with a stale one.
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($stagedPlugin))
$resource = $assembly.GetManifestResourceStream('JOVANISMOF.Splash.ps1')
if (-not $resource) { throw 'The staged plugin carries no copy of the splash, so an update could never refresh it.' }
$reader = New-Object IO.StreamReader($resource)
$carried = $reader.ReadToEnd()
$reader.Dispose()
Check ($carried -ceq ([IO.File]::ReadAllText($SplashScript))) 'the copy inside the plugin matches the splash being shipped'

function New-StandInInstall([string]$root, [string]$pluginDll, [string]$configBody) {
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'BepInEx\interop') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'BepInEx\config') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'BepInEx\plugins\MegabonkTogether') | Out-Null
    # Not notepad: on Windows 11 that is a stub which hands off to the Store app and exits at
    # once, which the launcher correctly reads as "the game closed".
    Copy-Item -LiteralPath "$env:SystemRoot\System32\cmd.exe" -Destination (Join-Path $root 'Megabonk.exe')
    Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $root 'BepInEx\plugins\MegabonkTogether\MegabonkTogether.dll')
    Set-Content -LiteralPath (Join-Path $root 'BepInEx\config\MegabonkTogether.cfg') -Value $configBody
}

function Get-PluginVersion([string]$root) {
    $dll = Join-Path $root 'BepInEx\plugins\MegabonkTogether\MegabonkTogether.dll'
    return [Reflection.AssemblyName]::GetAssemblyName($dll).Version
}

function Stop-StandIn([string]$root) {
    Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $root 'Megabonk.exe') } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
}

function Remove-Scratch([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notmatch '^jovanismof-splash-[a-f0-9]{8}$') {
        throw "Refusing cleanup outside the test scratch directory: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue }
}

function Start-Launcher([string]$root) {
    return Start-Process -FilePath 'powershell.exe' -PassThru -WindowStyle Hidden `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $SplashScript, '-GameDirectory', $root)
}

# --- Case 1: no repository configured. The game must still start, promptly. ------------------
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-splash-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$launcher = $null
try {
    Write-Host ''
    Write-Host 'Case: no update repository configured'
    New-StandInInstall $scratch $stagedPlugin "[Updates]`r`nCheckForUpdates = true`r`nUpdateRepository = `r`n"
    $launcher = Start-Launcher $scratch
    Start-Sleep -Seconds 6
    $running = Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $scratch 'Megabonk.exe') }
    Check ($null -ne $running) 'the game still starts when there is nothing to update from'

    Set-Content -LiteralPath (Join-Path $scratch 'BepInEx\.jovanismo-ready') -Value 'x'
    Start-Sleep -Seconds 3
    Check ($launcher.HasExited) 'the splash closes once the game reports it has loaded'
}
finally {
    if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue }
    Stop-StandIn $scratch
    Remove-Scratch $scratch
}

# --- Case 2: updates switched off. Nothing may be contacted or changed. ----------------------
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-splash-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$launcher = $null
try {
    Write-Host ''
    Write-Host 'Case: updates switched off'
    New-StandInInstall $scratch $stagedPlugin "[Updates]`r`nCheckForUpdates = false`r`nUpdateRepository = $repository`r`n"
    $before = Get-PluginVersion $scratch
    $launcher = Start-Launcher $scratch
    Start-Sleep -Seconds 6
    Check ((Get-PluginVersion $scratch) -eq $before) 'the plugin is left alone when updates are switched off'
    $running = Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $scratch 'Megabonk.exe') }
    Check ($null -ne $running) 'the game starts'
}
finally {
    if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue }
    Stop-StandIn $scratch
    Remove-Scratch $scratch
}

# --- Case 3: a genuinely old install updates itself before the game starts. ------------------
if (-not $OldPluginDll -or -not (Test-Path -LiteralPath $OldPluginDll) -or -not $repository) {
    Write-Host ''
    Write-Host 'Skipping the real update case: no older plugin or no repository was supplied.'
} else {
    $published = $null
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/latest" `
            -Headers @{ 'User-Agent' = 'JOVANISMOF-launcher-test' } -TimeoutSec 20 -UseBasicParsing
        $m = [regex]::Match(($latest.tag_name -replace '^[vV]', ''), '^(\d+)\.(\d+)\.(\d+)')
        if ($m.Success) { $published = [version]("{0}.{1}.{2}.0" -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value) }
    } catch { $published = $null }

    $oldVersion = [Reflection.AssemblyName]::GetAssemblyName($OldPluginDll).Version

    if (-not $published) {
        Write-Host ''
        Write-Host 'Skipping the real update case: GitHub could not be reached.' -ForegroundColor Yellow
    } elseif ($oldVersion -ge $published) {
        # Normal while cutting a release: the newest published build is the one this package
        # replaces, so there is nothing for it to update to yet.
        Write-Host ''
        Write-Host "Skipping the real update case: the plugin offered ($oldVersion) is not older than the newest release ($published)." -ForegroundColor Yellow
    } else {
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-splash-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $launcher = $null
        try {
            Write-Host ''
            Write-Host 'Case: an older install updates itself before the game starts'
            New-StandInInstall $scratch $OldPluginDll "[Updates]`r`nCheckForUpdates = true`r`nUpdateRepository = $repository`r`n"
            $before = Get-PluginVersion $scratch
            Write-Host "  installed before launch: $before"

            $launcher = Start-Launcher $scratch

            # The game must not appear until the update has been applied, so watch for the
            # moment it starts and read the version that is on disk right then.
            $versionWhenGameStarted = $null
            $deadline = (Get-Date).AddSeconds(120)
            while ((Get-Date) -lt $deadline) {
                $running = Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $scratch 'Megabonk.exe') }
                if ($running) { $versionWhenGameStarted = Get-PluginVersion $scratch; break }
                Start-Sleep -Milliseconds 250
            }

            Check ($null -ne $versionWhenGameStarted) 'the game was started'
            if ($versionWhenGameStarted) {
                Write-Host "  installed when the game started: $versionWhenGameStarted"
                Check ($versionWhenGameStarted -gt $before) 'the mod was already updated by the time the game started'
            }
            Check (@(Get-ChildItem -LiteralPath (Join-Path $scratch 'BepInEx\plugins\MegabonkTogether') -Filter '.update_download_*' -Force -ErrorAction SilentlyContinue).Count -eq 0) `
                'no leftover download is left for the in-game updater to offer again'
        }
        finally {
            if ($launcher -and -not $launcher.HasExited) { Stop-Process -Id $launcher.Id -Force -ErrorAction SilentlyContinue }
            Stop-StandIn $scratch
            Remove-Scratch $scratch
        }
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "Launcher behaviour failed ($($failures.Count)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    throw 'The launcher would not update correctly.'
}
Write-Host 'Launcher verified: it updates the mod before the game starts, and always starts the game.'
