# Shows a splash window while the game starts, then gets out of the way.
#
# The first launch after installing spends a long time with nothing on screen: BepInEx is
# generating interop assemblies for the game, and no mod code exists yet to draw anything.
# That work happens before the plugin loads, so the splash has to live out here in the
# launcher. The plugin writes a marker file once it is up, and that is what closes this.
#
# The plugin also checks for a newer build as it loads. That used to happen invisibly, so a
# player saw the game sit there with no idea anything was downloading. The plugin now writes
# what it is doing into a small status file and this window reports it.
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
$status.Text      = if ($firstLaunch) {
    "Preparing the mod for the first time.`r`nThis can take several minutes. Please do not close the game."
} else {
    'Starting Megabonk co-op...'
}
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

$game = Start-Process -FilePath $exe -WorkingDirectory $GameDirectory -PassThru
$started = Get-Date

# Give up eventually rather than leaving a splash pinned over someone's desktop for ever.
$limit = if ($firstLaunch) { [TimeSpan]::FromMinutes(20) } else { [TimeSpan]::FromMinutes(5) }

# Shared with the timer below, which runs in its own scope.
$state = [pscustomobject]@{
    Stage        = ''
    LastChange   = Get-Date
    Determinate  = $false
}

# The plugin rewrites this file as it goes, so a read can land mid-write. A failed read is
# not worth reporting: the next tick is 500ms away and will simply read it again.
function Read-UpdateStatus([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try {
        $map = @{}
        foreach ($line in [IO.File]::ReadAllLines($path)) {
            $split = $line.IndexOf('=')
            if ($split -gt 0) { $map[$line.Substring(0, $split)] = $line.Substring($split + 1) }
        }
        if (-not $map.ContainsKey('stage')) { return $null }
        return $map
    } catch { return $null }
}

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({
    $waited = (Get-Date) - $started
    $elapsed.Text = '{0:mm\:ss} elapsed' -f $waited

    $update = Read-UpdateStatus $updateFile
    $stage = if ($update) { $update['stage'] } else { '' }

    if ($stage -ne $state.Stage) {
        $state.Stage = $stage
        $state.LastChange = Get-Date
    }

    $busyUpdating = $false
    switch ($stage) {
        'checking' {
            $status.Text = 'Checking for updates...'
            $busyUpdating = $true
        }
        'downloading' {
            $busyUpdating = $true
            $version = $update['version']
            $percent = -1
            if ($update.ContainsKey('percent')) { [void][int]::TryParse($update['percent'], [ref]$percent) }

            if ($percent -ge 0) {
                if (-not $state.Determinate) {
                    $bar.Style = 'Continuous'
                    $bar.Minimum = 0
                    $bar.Maximum = 100
                    $state.Determinate = $true
                }
                $bar.Value = [Math]::Min(100, [Math]::Max(0, $percent))
                $status.Text = "Downloading update $version... $percent%"
                # A percentage that keeps moving is proof the download is alive.
                $state.LastChange = Get-Date
            } else {
                $status.Text = "Downloading update $version..."
            }
        }
        'ready' {
            $status.Text = "Update $($update['version']) downloaded.`r`nIt installs when you quit the game."
        }
        'failed' {
            # Not fatal: the game is already starting, and it will try again next launch.
            $status.Text = "Could not check for updates.`r`nStarting the game anyway."
        }
    }

    $ready = Test-Path -LiteralPath $marker
    $gone  = $game.HasExited

    # A download that has produced nothing for a while is treated as finished, so a stalled
    # update can never leave this window sitting on top of the game.
    if ($busyUpdating -and ((Get-Date) - $state.LastChange) -gt [TimeSpan]::FromSeconds(90)) {
        $busyUpdating = $false
    }

    if ($gone -or $waited -gt $limit) {
        $timer.Stop()
        $form.Close()
        return
    }

    # Stay up through the update so the player can see it happening, even though the plugin
    # has finished loading by then.
    if ($ready -and -not $busyUpdating) {
        $timer.Stop()
        $form.Close()
    }
})
$timer.Start()

[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
