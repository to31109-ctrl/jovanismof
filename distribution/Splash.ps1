# Shows a splash window while the game starts, and updates the mod before it does.
#
# Two jobs:
#
# 1. The first launch after installing spends a long time with nothing on screen: BepInEx is
#    generating interop assemblies for the game, and no mod code exists yet to draw anything.
#    That work happens before the plugin loads, so the splash has to live out here.
#
# 2. Updating. This used to be done by the plugin from inside the game: it downloaded while
#    you played, then asked you to quit, and a batch file swapped the files once the game had
#    closed. That meant quitting and relaunching to get a build you had already downloaded.
#    Updating here instead means nothing is holding the files open, so the update is simply
#    applied before the game starts and the player launches straight into the new version.
#
# Nothing in the update path may stop the game starting. Every failure falls through to
# launching what is already installed.
param(
    [Parameter(Mandatory = $true)][string]$GameDirectory
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $GameDirectory 'Megabonk.exe'
if (-not (Test-Path -LiteralPath $exe)) { exit 1 }

$marker = Join-Path $GameDirectory 'BepInEx\.jovanismo-ready'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue }

# Left over from the previous launch, this would otherwise be read as news about this one.
$updateFile = Join-Path $GameDirectory 'BepInEx\.jovanismof-update'
if (Test-Path -LiteralPath $updateFile) { Remove-Item -LiteralPath $updateFile -Force -ErrorAction SilentlyContinue }

$pluginDirectory = Join-Path $GameDirectory 'BepInEx\plugins\MegabonkTogether'
$configFile = Join-Path $GameDirectory 'BepInEx\config\MegabonkTogether.cfg'

# A first launch has no interop cache yet, and takes minutes rather than seconds.
$firstLaunch = -not (Test-Path -LiteralPath (Join-Path $GameDirectory 'BepInEx\interop'))

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form                 = New-Object Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition   = 'CenterScreen'
$form.Size            = New-Object Drawing.Size(520, 240)
$form.BackColor       = [Drawing.Color]::FromArgb(18, 20, 24)
$form.TopMost         = $true
$form.ShowInTaskbar   = $true
$form.Text            = 'JOVANISMOF'

$title           = New-Object Windows.Forms.Label
$title.Text      = 'JOVANISMOF'
$title.Font      = New-Object Drawing.Font('Segoe UI', 30, [Drawing.FontStyle]::Bold)
$title.ForeColor = [Drawing.Color]::White
$title.AutoSize  = $false
$title.TextAlign = 'MiddleCenter'
$title.Dock      = 'Top'
$title.Height    = 90
$form.Controls.Add($title)

$status           = New-Object Windows.Forms.Label
$status.Font      = New-Object Drawing.Font('Segoe UI', 11)
$status.ForeColor = [Drawing.Color]::FromArgb(190, 195, 205)
$status.AutoSize  = $false
$status.TextAlign = 'MiddleCenter'
$status.Dock      = 'Top'
$status.Height    = 60
$status.Text      = 'Starting...'
$form.Controls.Add($status)

$bar          = New-Object Windows.Forms.ProgressBar
$bar.Style    = 'Marquee'
$bar.Dock     = 'Top'
$bar.Height   = 18
$bar.MarqueeAnimationSpeed = 30
$form.Controls.Add($bar)

$elapsed           = New-Object Windows.Forms.Label
$elapsed.Font      = New-Object Drawing.Font('Segoe UI', 9)
$elapsed.ForeColor = [Drawing.Color]::FromArgb(130, 135, 145)
$elapsed.AutoSize  = $false
$elapsed.TextAlign = 'MiddleCenter'
$elapsed.Dock      = 'Bottom'
$elapsed.Height    = 30
$form.Controls.Add($elapsed)

$form.Show()
$form.Refresh()

function Set-Status([string]$text) {
    $status.Text = $text
    $status.Refresh()
    [Windows.Forms.Application]::DoEvents()
}

function Set-Indeterminate {
    if ($bar.Style -ne 'Marquee') { $bar.Style = 'Marquee' }
    [Windows.Forms.Application]::DoEvents()
}

function Set-Percent([int]$percent) {
    if ($bar.Style -ne 'Continuous') {
        $bar.Style = 'Continuous'
        $bar.Minimum = 0
        $bar.Maximum = 100
    }
    $bar.Value = [Math]::Min(100, [Math]::Max(0, $percent))
    [Windows.Forms.Application]::DoEvents()
}

# Reads a key only where BepInEx would read it: inside the named section.
function Get-ConfigValue([string]$path, [string]$section, [string]$key) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $text = [IO.File]::ReadAllText($path)
    $m = [regex]::Match($text, '(?ms)^\[' + $section + '\][^\r\n]*\r?\n(?:(?!^\[).)*')
    if (-not $m.Success) { return $null }
    $k = [regex]::Match($m.Value, '(?m)^' + $key + '\s*=[ \t]*([^\r\n]*)')
    if (-not $k.Success) { return $null }
    return $k.Groups[1].Value.Trim()
}

# Compared on three parts only. An assembly reports 5.1.4.0 while a release is tagged 5.1.4,
# and .NET considers the four-part one newer, which would make an up-to-date copy look stale.
function ConvertTo-ComparableVersion([string]$text) {
    if (-not $text) { return $null }
    $text = $text.Trim().TrimStart('v', 'V')
    $m = [regex]::Match($text, '^(\d+)\.(\d+)\.(\d+)')
    if (-not $m.Success) { return $null }
    return [version]("{0}.{1}.{2}" -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value)
}

function Get-InstalledVersion([string]$pluginDirectory) {
    $dll = Join-Path $pluginDirectory 'MegabonkTogether.dll'
    if (-not (Test-Path -LiteralPath $dll)) { return $null }
    # Reads the manifest without loading the assembly, so the file is never locked.
    return ConvertTo-ComparableVersion ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString())
}

function Invoke-LauncherUpdate {
    $enabled = Get-ConfigValue $configFile 'Updates' 'CheckForUpdates'
    $repository = Get-ConfigValue $configFile 'Updates' 'UpdateRepository'

    if ($enabled -and $enabled -eq 'false') { return }
    if (-not $repository -or $repository -notmatch '^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$') { return }

    $installed = Get-InstalledVersion $pluginDirectory
    if (-not $installed) { return }

    Set-Status 'Checking for updates...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $headers = @{ 'User-Agent' = 'JOVANISMOF-launcher'; 'Accept' = 'application/vnd.github+json' }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/latest" `
        -Headers $headers -TimeoutSec 20 -UseBasicParsing

    $remote = ConvertTo-ComparableVersion $release.tag_name
    if (-not $remote -or $remote -le $installed) { return }

    # Same rule the plugin uses, so the full installer archive is never mistaken for an update.
    $asset = $release.assets | Where-Object { $_.name -match '^[A-Za-z0-9._\-]+\-\d+\.\d+\.\d+\.zip$' } | Select-Object -First 1
    if (-not $asset) { return }

    $temp = Join-Path ([IO.Path]::GetTempPath()) ("jovanismof-update-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $temp | Out-Null
    try {
        $archive = Join-Path $temp $asset.name
        Set-Status "Downloading update $($release.tag_name)..."

        $request = [Net.HttpWebRequest]::Create($asset.browser_download_url)
        $request.UserAgent = 'JOVANISMOF-launcher'
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 60000
        $response = $request.GetResponse()
        try {
            $total = $response.ContentLength
            $source = $response.GetResponseStream()
            $target = [IO.File]::Create($archive)
            try {
                $buffer = New-Object byte[] 81920
                $received = 0L
                $lastShown = -1
                while (($read = $source.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    $target.Write($buffer, 0, $read)
                    $received += $read
                    if ($total -gt 0) {
                        $percent = [int]($received * 100 / $total)
                        if ($percent -ne $lastShown) {
                            $lastShown = $percent
                            Set-Percent $percent
                            Set-Status "Downloading update $($release.tag_name)... $percent%"
                        }
                    } else {
                        [Windows.Forms.Application]::DoEvents()
                    }
                }
            } finally { $target.Dispose(); $source.Dispose() }
        } finally { $response.Dispose() }

        Set-Indeterminate
        Set-Status "Installing update $($release.tag_name)..."

        # Unpacked somewhere harmless first. A half-extracted archive copied straight over the
        # plugin would leave an installation that cannot load at all.
        $unpacked = Join-Path $temp 'unpacked'
        New-Item -ItemType Directory -Force -Path $unpacked | Out-Null
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($entry in $zip.Entries) {
                if (-not $entry.Name) { continue }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $unpacked $entry.Name), $true)
            }
        } finally { $zip.Dispose() }

        if (-not (Test-Path -LiteralPath (Join-Path $unpacked 'MegabonkTogether.dll'))) { return }

        Copy-Item -Path (Join-Path $unpacked '*') -Destination $pluginDirectory -Force -Recurse

        # The in-game updater would otherwise find this and offer an update already applied.
        Get-ChildItem -LiteralPath $pluginDirectory -Filter '.update_download_*' -Force -ErrorAction SilentlyContinue |
            Remove-Item -Force -ErrorAction SilentlyContinue

        Set-Status "Updated to $($release.tag_name)."
    }
    finally {
        $full = [IO.Path]::GetFullPath($temp)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
        if ($full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($full) -match '^jovanismof-update-[a-f0-9]{8}$') {
            Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# An update is a convenience. Whatever goes wrong, the player still gets their game.
try { Invoke-LauncherUpdate }
catch { Set-Status "Could not check for updates.`r`nStarting the game anyway." ; Start-Sleep -Milliseconds 1200 }

Set-Indeterminate
$status.Text = if ($firstLaunch) {
    "Preparing the mod for the first time.`r`nThis can take several minutes. Please do not close the game."
} else {
    'Starting Megabonk co-op...'
}
$status.Refresh()

$game = Start-Process -FilePath $exe -WorkingDirectory $GameDirectory -PassThru
$started = Get-Date

# Give up eventually rather than leaving a splash pinned over someone's desktop for ever.
$limit = if ($firstLaunch) { [TimeSpan]::FromMinutes(20) } else { [TimeSpan]::FromMinutes(5) }

while ($true) {
    [Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 250

    $waited = (Get-Date) - $started
    $elapsed.Text = '{0:mm\:ss} elapsed' -f $waited

    if ($game.HasExited -or (Test-Path -LiteralPath $marker) -or $waited -gt $limit) { break }
}

$form.Close()
$form.Dispose()
