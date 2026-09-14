# Drives the splash through every update state against a stand-in game, because the thing
# that matters -- whether the window stays up while an update downloads -- cannot be seen by
# reading the script.
param(
    [Parameter(Mandatory = $true)][string]$StageDirectory
)
$ErrorActionPreference = 'Stop'

$SplashScript = Join-Path $StageDirectory 'Splash.ps1'
if (!(Test-Path -LiteralPath $SplashScript)) { throw "No Splash.ps1 was staged at $SplashScript." }

$failures = @()
function Check([bool]$ok, [string]$what) {
    if ($ok) { Write-Host "  ok   $what" } else { Write-Host "  FAIL $what" -ForegroundColor Red; $script:failures += $what }
}

# The plugin carries its own copy of the splash and writes it out beside the game, because
# an update cannot reach a file outside the plugin folder. If that copy ever drifts from the
# staged one, installing an update would quietly replace a good splash with a stale one.
$stagedPlugin = Join-Path $StageDirectory 'payload\BepInEx\plugins\MegabonkTogether\MegabonkTogether.dll'
if (Test-Path -LiteralPath $stagedPlugin) {
    # Loaded from bytes so the staged file is not locked for the rest of the build.
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($stagedPlugin))
    $resource = $assembly.GetManifestResourceStream('JOVANISMOF.Splash.ps1')
    if (-not $resource) {
        throw 'The staged plugin carries no copy of the splash, so an update could never refresh it.'
    }
    $reader = New-Object IO.StreamReader($resource)
    $carried = $reader.ReadToEnd()
    $reader.Dispose()
    $staged = [IO.File]::ReadAllText($SplashScript)
    Check ($carried -ceq $staged) 'the copy inside the plugin matches the splash being shipped'
} else {
    throw "No staged plugin found at $stagedPlugin."
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-splash-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$splashProcess = $null
$stand = $null
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'BepInEx\interop') | Out-Null
    # A stand-in that simply stays alive, so the splash has a "game" to watch. Not notepad:
    # on Windows 11 that is a stub which hands off to the Store app and exits at once,
    # which the splash correctly reads as "the game closed".
    Copy-Item -LiteralPath "$env:SystemRoot\System32\cmd.exe" -Destination (Join-Path $scratch 'Megabonk.exe')

    $marker = Join-Path $scratch 'BepInEx\.jovanismo-ready'
    $statusFile = Join-Path $scratch 'BepInEx\.jovanismof-update'
    # Left over from a previous launch: the splash must clear this, not believe it.
    Set-Content -LiteralPath $statusFile -Value "stage=ready`nversion=9.9.9"

    $splashProcess = Start-Process -FilePath 'powershell.exe' -PassThru -WindowStyle Hidden `
        -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $SplashScript, '-GameDirectory', $scratch)

    Start-Sleep -Seconds 4
    Check (-not $splashProcess.HasExited) 'splash is up'
    $stand = Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue
    Check ($null -ne $stand) 'the splash started the game'
    Check (-not (Test-Path -LiteralPath $statusFile)) 'a stale status file from the previous launch was cleared'

    # The plugin has loaded and a check is starting. The splash must NOT close here, even
    # though loading is finished -- that is the whole point of the change.
    Set-Content -LiteralPath $statusFile -Value 'stage=checking'
    Set-Content -LiteralPath $marker -Value '5.1.4'
    Start-Sleep -Seconds 3
    Check (-not $splashProcess.HasExited) 'stays up while checking for updates, after loading finished'

    foreach ($percent in @(0, 35, 80)) {
        Set-Content -LiteralPath $statusFile -Value "stage=downloading`nversion=5.1.4`npercent=$percent"
        Start-Sleep -Seconds 2
        Check (-not $splashProcess.HasExited) "stays up while downloading ($percent%)"
    }

    # An update with no content length must not close it either.
    Set-Content -LiteralPath $statusFile -Value "stage=downloading`nversion=5.1.4`npercent=-1"
    Start-Sleep -Seconds 2
    Check (-not $splashProcess.HasExited) 'stays up while downloading with no known size'

    # Downloaded: now it is allowed to get out of the way.
    Set-Content -LiteralPath $statusFile -Value "stage=ready`nversion=5.1.4"
    Start-Sleep -Seconds 4
    Check ($splashProcess.HasExited) 'closes once the update is downloaded and the game is loaded'
}
finally {
    if ($splashProcess -and -not $splashProcess.HasExited) {
        Stop-Process -Id $splashProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host '  (splash had to be forced closed)' -ForegroundColor Yellow
    }
    Get-Process -Name 'Megabonk' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq (Join-Path $scratch 'Megabonk.exe') } |
        ForEach-Object { Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue }
    $full = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($full) -notmatch '^jovanismof-splash-[a-f0-9]{8}$') {
        throw "Refusing cleanup outside the test scratch directory: $full"
    }
    if (Test-Path -LiteralPath $full) { Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "Splash behaviour failed ($($failures.Count)):" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    throw 'The splash would not report an update correctly.'
}
Write-Host 'Splash verified: it reports the update and stays up until the download is done.'
