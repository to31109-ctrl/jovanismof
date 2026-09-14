# Installs the staged package into a throwaway folder and checks that the launcher can
# actually find the game, which is the failure a player sees as:
#
#     Could not find Megabonk.exe in: <path>
#
# Reading the source is not enough here. That error came from a byte-order mark written
# into the path file, so the only honest check is to install and resolve the path for real.
param(
    [Parameter(Mandatory = $true)][string]$StageDirectory,
    [Parameter(Mandatory = $true)][string]$GamePath
)
$ErrorActionPreference = 'Stop'

$scratch = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-verify-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Play JOVANISMOF.lnk'
$desktopExisted = Test-Path -LiteralPath $desktopLink
$desktopBackup = if ($desktopExisted) { [IO.File]::ReadAllBytes($desktopLink) } else { $null }
$stageLink = Join-Path $StageDirectory 'Play JOVANISMOF.lnk'
$stageBackup = if (Test-Path -LiteralPath $stageLink) { [IO.File]::ReadAllBytes($stageLink) } else { $null }

try {
    New-Item -ItemType Directory -Force -Path (Join-Path $scratch 'Megabonk_Data') | Out-Null
    Copy-Item -LiteralPath (Join-Path $GamePath 'Megabonk.exe') -Destination $scratch -Force
    'placeholder' | Set-Content -LiteralPath (Join-Path $scratch 'Megabonk_Data\verify.txt')

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $StageDirectory 'Install.ps1') -GamePath $scratch | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The installer failed against a clean folder (exit $LASTEXITCODE)." }

    $installed = Join-Path $scratch 'BonkLink-Coop'
    $pathFile = Join-Path $installed 'gamepath.txt'
    if (!(Test-Path -LiteralPath $pathFile)) { throw 'The installer did not write gamepath.txt.' }

    # A byte-order mark here is exactly what made the launcher look in a folder that cannot exist.
    $bytes = [IO.File]::ReadAllBytes($pathFile)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        throw 'gamepath.txt starts with a byte-order mark; the launcher would not find the game.'
    }

    $resolved = ([IO.File]::ReadAllText($pathFile)).Trim()
    if (!(Test-Path -LiteralPath (Join-Path $resolved 'Megabonk.exe'))) {
        throw "The launcher would look in '$resolved', where there is no Megabonk.exe."
    }

    foreach ($needed in @('Play.vbs', 'Splash.ps1')) {
        if (!(Test-Path -LiteralPath (Join-Path $installed $needed))) {
            throw "$needed was not installed beside the game, so the shortcut would break."
        }
    }

    $shortcut = Join-Path $StageDirectory 'Play JOVANISMOF.lnk'
    if (!(Test-Path -LiteralPath $shortcut)) { throw 'No shortcut was created in the extracted folder.' }

    Write-Host "Launcher verified: resolves to $resolved with no byte-order mark, shortcut present."
}
finally {
    if ($desktopExisted) {
        [IO.File]::WriteAllBytes($desktopLink, $desktopBackup)
    } elseif (Test-Path -LiteralPath $desktopLink) {
        Remove-Item -LiteralPath $desktopLink -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $stageBackup) {
        [IO.File]::WriteAllBytes($stageLink, $stageBackup)
    } elseif (Test-Path -LiteralPath $stageLink) {
        Remove-Item -LiteralPath $stageLink -Force
    }
    $scratchFullPath = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (!$scratchFullPath.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($scratchFullPath) -notmatch '^jovanismof-verify-[a-f0-9]{8}$') {
        throw "Refusing cleanup outside the verification scratch directory: $scratchFullPath"
    }
    if (Test-Path -LiteralPath $scratchFullPath) { Remove-Item -LiteralPath $scratchFullPath -Recurse -Force -ErrorAction SilentlyContinue }
}
