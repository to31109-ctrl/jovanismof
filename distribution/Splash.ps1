# Shows a splash window while the game starts, then gets out of the way.
#
# The first launch after installing spends a long time with nothing on screen: BepInEx is
# generating interop assemblies for the game, and no mod code exists yet to draw anything.
# That work happens before the plugin loads, so the splash has to live out here in the
# launcher. The plugin writes a marker file once it is up, and that is what closes this.
param(
    [Parameter(Mandatory = $true)][string]$GameDirectory
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $GameDirectory 'Megabonk.exe'
if (-not (Test-Path -LiteralPath $exe)) { exit 1 }

$marker = Join-Path $GameDirectory 'BepInEx\.jovanismo-ready'
if (Test-Path -LiteralPath $marker) { Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue }

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

$timer = New-Object Windows.Forms.Timer
$timer.Interval = 500
$timer.Add_Tick({
    $waited = (Get-Date) - $started
    $elapsed.Text = '{0:mm\:ss} elapsed' -f $waited

    $ready = Test-Path -LiteralPath $marker
    $gone  = $game.HasExited

    if ($ready -or $gone -or $waited -gt $limit) {
        $timer.Stop()
        $form.Close()
    }
})
$timer.Start()

[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()
