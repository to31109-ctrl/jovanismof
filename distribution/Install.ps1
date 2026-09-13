param([string]$GamePath)
$ErrorActionPreference = 'Stop'
try {
    if (!$GamePath) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Select your Megabonk folder (the folder containing Megabonk.exe)'
        if ($picker.ShowDialog() -ne 'OK') { exit }
        $GamePath = $picker.SelectedPath
    }
    $GamePath = (Resolve-Path -LiteralPath $GamePath).Path
    if (!(Test-Path -LiteralPath (Join-Path $GamePath 'Megabonk.exe')) -or !(Test-Path -LiteralPath (Join-Path $GamePath 'Megabonk_Data'))) {
        throw 'Select the game installation folder containing Megabonk.exe and Megabonk_Data.'
    }
    $target = Join-Path $GamePath 'BonkLink-Coop'
    $marker = Join-Path $target '.bonklink-install'
    if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $marker)) { throw "$target already exists and is not a BonkLink installation. Rename it first." }
    $running = Get-Process Megabonk -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target 'Megabonk.exe') }
    if ($running) { throw 'Close the BonkLink game before installing an update.' }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Set-Content -LiteralPath $marker -Value 'BonkLink edition of Megabonk Together'
    Write-Host 'Preparing a separate co-op game folder...'
    $assets = @(Get-ChildItem -LiteralPath (Join-Path $GamePath 'Megabonk_Data') -Recurse -File)
    foreach ($name in @('Megabonk.exe','GameAssembly.dll','UnityPlayer.dll','baselib.dll','steam_appid.txt')) {
        $file = Join-Path $GamePath $name
        if (Test-Path -LiteralPath $file) { $assets += Get-Item -LiteralPath $file }
    }
    foreach ($asset in $assets) {
        $relative = $asset.FullName.Substring($GamePath.Length).TrimStart('\')
        $destination = Join-Path $target $relative
        if (Test-Path -LiteralPath $destination) { continue }
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
        try { New-Item -ItemType HardLink -Path $destination -Target $asset.FullName | Out-Null }
        catch { Copy-Item -LiteralPath $asset.FullName -Destination $destination }
    }
    Copy-Item -Path (Join-Path $PSScriptRoot 'payload\*') -Destination $target -Recurse -Force
    $configRoot = Join-Path $target 'BepInEx\config'
    New-Item -ItemType Directory -Force -Path $configRoot | Out-Null

    # Players should see the game, not a console window scrolling log output. The log is still
    # written to BepInEx/LogOutput.log, which is what to send if something needs diagnosing.
    $bepInExConfig = Join-Path $configRoot 'BepInEx.cfg'
    if (Test-Path -LiteralPath $bepInExConfig) {
        $existing = Get-Content -LiteralPath $bepInExConfig -Raw
        $updated = [regex]::Replace($existing, '(?m)^(\[Logging\.Console\][\s\S]*?^Enabled\s*=\s*)true', '${1}false')
        if ($updated -ne $existing) { Set-Content -LiteralPath $bepInExConfig -Value $updated -Encoding UTF8 }
    } else {
        @'
[Logging.Console]
Enabled = false

[Logging.Disk]
Enabled = true
'@ | Set-Content -LiteralPath $bepInExConfig -Encoding UTF8
    }
    $updateRepository = ''
    $repositoryFile = Join-Path $PSScriptRoot 'update-repository.txt'
    if (Test-Path -LiteralPath $repositoryFile) { $updateRepository = (Get-Content -LiteralPath $repositoryFile -Raw).Trim() }

    $configFile = Join-Path $configRoot 'MegabonkTogether.cfg'
    if (!(Test-Path -LiteralPath $configFile)) {
        @"
[Gameplay]
EnabledSharedExperience = true
AllowSavesDuringNetplay = false
[Updates]
CheckForUpdates = true
UpdateRepository = $updateRepository
ShowChangelog = false
PreviousVersion = 5.1.0
"@ | Set-Content -LiteralPath $configFile -Encoding UTF8
    } elseif ($updateRepository) {
        # An existing installation should still learn where updates come from.
        $config = Get-Content -LiteralPath $configFile -Raw
        if ($config -match '(?m)^UpdateRepository\s*=') {
            $config = [regex]::Replace($config, '(?m)^UpdateRepository\s*=.*$', "UpdateRepository = $updateRepository")
        } else {
            $config = $config.TrimEnd() + "`r`nUpdateRepository = $updateRepository`r`n"
        }
        Set-Content -LiteralPath $configFile -Value $config -Encoding UTF8
    }
    # The launcher shows a splash while BepInEx prepares itself, because the first launch
    # sits on a black screen for minutes before any mod code exists to draw anything. It is
    # installed next to the game so the extracted archive can be deleted afterwards.
    foreach ($name in @('Play.vbs', 'Splash.ps1')) {
        $from = Join-Path $PSScriptRoot $name
        if (Test-Path -LiteralPath $from) { Copy-Item -LiteralPath $from -Destination (Join-Path $target $name) -Force }
    }
    # Written without a byte-order mark: Set-Content -Encoding UTF8 prefixes one, and the
    # launcher reads this as plain text, so the mark ends up glued to the front of the
    # path and nothing can be found there.
    [IO.File]::WriteAllText((Join-Path $target 'gamepath.txt'), $target, (New-Object System.Text.UTF8Encoding($false)))

    # A shortcut is a convenience, so a failure here must never fail the installation and
    # must never leave the player with no way to start the game.
    $launcher = Join-Path $target 'Play.vbs'
    $useLauncher = Test-Path -LiteralPath $launcher

    function New-PlayShortcut([string]$LinkPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($LinkPath)
        if ($useLauncher) {
            $shortcut.TargetPath = "$env:SystemRoot\System32\wscript.exe"
            $shortcut.Arguments = '"' + $launcher + '"'
        } else {
            $shortcut.TargetPath = Join-Path $target 'Megabonk.exe'
        }
        $shortcut.WorkingDirectory = $target
        $shortcut.IconLocation = (Join-Path $target 'Megabonk.exe') + ',0'
        $shortcut.Save()
    }

    $madeShortcut = $false
    # The extracted folder is where the player just double-clicked Install, so it is the
    # first place they look. The copies beside the game and on the Desktop keep working
    # even after the extracted folder is deleted.
    foreach ($place in @($PSScriptRoot, $target, [Environment]::GetFolderPath('Desktop'))) {
        if (!$place) { continue }
        try {
            New-PlayShortcut (Join-Path $place 'Play JOVANISMOF.lnk')
            $madeShortcut = $true
            Write-Host "Shortcut created: $(Join-Path $place 'Play JOVANISMOF.lnk')"
        } catch {
            Write-Host "Could not create a shortcut in $place : $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    # Something double-clickable exists even if Windows refused to make a shortcut at all.
    $fallback = Join-Path $target 'Play JOVANISMOF.cmd'
    $newline = [char]13 + [string][char]10
    if ($useLauncher) {
        $fallbackBody = '@echo off' + $newline + 'start "" wscript.exe "%~dp0Play.vbs"'
    } else {
        $fallbackBody = '@echo off' + $newline + 'start "" "' + (Join-Path $target 'Megabonk.exe') + '"'
    }
    Set-Content -LiteralPath $fallback -Value $fallbackBody -Encoding ASCII

    # An older install may still have the previous shortcut sitting next to the new one.
    $stale = Join-Path $PSScriptRoot 'Play BonkLink.lnk'
    if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force -ErrorAction SilentlyContinue }

    if ($madeShortcut) {
        Write-Host "Installed. Open Play JOVANISMOF on your Desktop, or in $target"
    } else {
        Write-Host "Installed. Open Play JOVANISMOF.cmd in $target"
    }
    Write-Host 'The mod is installed with the game, so this extracted folder can be deleted later.'
    Write-Host 'Use JOVANISMOF > Friendlies > Host or Join. Share the private room code.'
    Write-Host 'The first launch may take several minutes while BepInEx generates game interfaces.'
} catch {
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
