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
        # Same family, and it cost a whole test run: thrown inside a coroutine it stopped the
        # UI check reporting anything at all, which read as the check simply not running.
        # A rule that reads its own explanatory comment as a fault cries wolf.
        # The harness never ships, so this rule guards the plugin players actually run.
        if ($file.Name -ne 'BonkLinkSmoke.cs' -and $line -notmatch '^\s*(//|\*)' -and ($line -match 'FindObjectsOfType<' -or $line -match 'FindObjectOfType<')) {
            $faults += "$($file.Name):$n calls FindObjectsOfType<T>(), which throws under IL2CPP. Ask the object that owns them instead."
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

# 5. CustomButton derives from the game's MyButton, and it is the game's own button machinery
#    that calls OnClick. Added to a GameObject built from scratch it compiles, renders, and
#    never responds to a click. Every working button on the mod's menus is a clone of a real
#    one (GameObject.Instantiate) with MyButtonNormal stripped off. This is what left the
#    world list's rows and Delete buttons dead on screen.
#
#    Judged by the nearest preceding assignment to that same variable, so a variable named
#    the same thing in another method cannot make this fire.
foreach ($file in $source) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $hit = [regex]::Match($lines[$i], '(\w+)\s*\.AddComponent<CustomButton>')
        if (!$hit.Success) { continue }
        $name = $hit.Groups[1].Value

        $builtFromNothing = $false
        for ($j = $i; $j -ge 0 -and $j -gt $i - 60; $j--) {
            $assigned = [regex]::Match($lines[$j], ('(?:var|GameObject)\s+' + [regex]::Escape($name) + '\s*=\s*(.+)$'))
            if (!$assigned.Success) { continue }
            $builtFromNothing = $assigned.Groups[1].Value -match 'new\s+GameObject\s*\('
            break
        }

        if ($builtFromNothing) {
            $faults += "$($file.Name):$($i + 1) adds a CustomButton to '$name', built with new GameObject(). It will render and never receive a click. Clone a real menu button instead."
        }
    }
}

# 6. Anything the mod adds to the netplay panel belongs to one screen inside it. Built
#    without being switched off, it is visible the moment the menu opens: the world list
#    appeared over the main menu, its rows hidden behind the menu's own buttons and its
#    Delete buttons hanging off the side of the panel.
$menu = Join-Path $repo 'src/plugin/Scripts/Modal/NetworkMenuTab.cs'
if (Test-Path -LiteralPath $menu) {
    $menuText = Get-Content -Raw -LiteralPath $menu
    foreach ($element in @('worldChooseButton.gameObject', 'worldDoneButton.gameObject', 'worldScreenTitle', 'worldNameRow', 'worldListRoot')) {
        if ($menuText -notmatch ([regex]::Escape($element) + '\.SetActive\(\$?false\)')) {
            $faults += "NetworkMenuTab.cs never hides '$element' when it is built, so it shows over the main menu."
        }
    }
}

# 7. A test build must never be shipped: it redirects saves and quits on a timer.
foreach ($file in $source) {
    if ($file.Name -eq 'BonkLinkSmoke.cs') { continue }
    if ((Get-Content -Raw -LiteralPath $file.FullName) -match '#define\s+BONKLINK_TESTING') {
        $faults += "$($file.Name) defines BONKLINK_TESTING in source."
    }
}

# 8. Nothing may take the player's controls away because an update exists. A release published
#    while somebody was playing left them able to walk but unable to jump, interact, or open the
#    pause menu -- so they could not even quit to apply the update they were being punished for
#    not having. The launcher updates before the game starts; in-game this is only ever harm.
foreach ($file in $source) {
    $text = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $text.Count; $i++) {
        if ($text[$i] -match '^\s*//') { continue }
        if ($text[$i] -notmatch 'IsAnUpdateAvailable\s*\(') { continue }
        # Looking within the method for a swallowed input result.
        for ($j = $i; $j -lt [Math]::Min($text.Count, $i + 12); $j++) {
            if ($text[$j] -match '__result\s*=\s*false') {
                $faults += "$($file.Name):$($i + 1) refuses the player's input because an update is available. That leaves them unable to jump, interact or open the pause menu."
                break
            }
        }
    }
}

# 9. Every step of the networking frame must be run on its own. They were once a single block,
#    and one failing call -- the host's lobby broadcast -- stopped everything after it: remote
#    players froze at spawn, no enemies or projectiles were sent, no checkpoints were written.
$handler = Join-Path $repo 'src/plugin/Scripts/NetworkHandler.cs'
if (Test-Path -LiteralPath $handler) {
    $handlerText = Get-Content -Raw -LiteralPath $handler
    foreach ($step in @('udpClientService.Update', 'udpClientService.UpdateEnemies', 'udpClientService.UpdateProjectiles', 'RecoverFromAStuckChoice')) {
        if ($handlerText -notmatch ('Step\("[^"]+",\s*' + [regex]::Escape($step))) {
            $faults += "NetworkHandler.cs calls $step outside Step(), so a fault in it stops every part of the frame that follows."
        }
    }
}

# 10. The colour a player is known by is also their marker on the minimap and the arrow pointing
#     at them. Half-transparent ones read as grey over the map, and one of the six literally was
#     grey, so players reported each other as "barely visible" and "not a bright colour".
$card = Join-Path $repo 'src/plugin/Scripts/NetPlayer/NetPlayerCard.cs'
if (Test-Path -LiteralPath $card) {
    $cardText = Get-Content -Raw -LiteralPath $card
    $palette = [regex]::Match($cardText, 'MarkerColors\s*=\s*\[(?<body>[^\]]*)\]')
    if (!$palette.Success) {
        $faults += 'NetPlayerCard.cs no longer declares MarkerColors, so nothing checks the markers are visible.'
    } else {
        foreach ($entry in [regex]::Matches($palette.Groups['body'].Value, 'new\s+Color\(([^)]*)\)')) {
            $parts = $entry.Groups[1].Value -split ',' | ForEach-Object { [double]($_ -replace '[fF]\s*$', '').Trim() }
            if ($parts.Count -ge 4 -and $parts[3] -lt 1) {
                $faults += "NetPlayerCard.cs has a see-through player colour ($($entry.Value)); on a minimap that reads as grey."
            }
            if ($parts.Count -ge 3) {
                $spread = ($parts[0..2] | Measure-Object -Maximum -Minimum)
                if (($spread.Maximum - $spread.Minimum) -lt 0.2) {
                    $faults += "NetPlayerCard.cs has a colourless player colour ($($entry.Value)); it cannot be told apart on the map."
                }
            }
        }
    }
}

# 11. Shielding a player from somebody else's purchase must not also make their own purchases
#     free. The shield is a twenty-second clock opened by any replayed interaction, and with a
#     party opening chests it is open almost permanently -- so gold stopped costing anything.
#     The player's own keypress is what tells the two apart.
$wallet = Join-Path $repo 'src/plugin/Patches/Inventories/PlayerInventory.cs'
if (Test-Path -LiteralPath $wallet) {
    $walletText = Get-Content -Raw -LiteralPath $wallet
    if ($walletText -match 'ReplayShieldUntil' -and $walletText -notmatch 'OwnPurchaseUntil') {
        $faults += 'PlayerInventory.cs skips a debit during the replay shield without asking whether this player pressed the key themselves, so their own purchases are free too.'
    }
}
$detect = Join-Path $repo 'src/plugin/Patches/DetectInteractables.cs'
if (Test-Path -LiteralPath $detect) {
    $detectText = Get-Content -Raw -LiteralPath $detect
    if ($detectText -notmatch 'OwnPurchaseUntil\s*=') {
        $faults += 'DetectInteractables.cs never records that this player pressed the interact key, so the replay shield cannot tell their own purchases from a replayed one.'
    }

# 12. A player who is briefly untouchable must still be able to use things. That invulnerability
#     is the game's teleporting flag, which also switches interaction off -- so the interact key
#     did nothing for most of a run: 181 refusals in one player's log.
    $guard = [regex]::Match($detectText, 'private static bool CanSynchronize[\s\S]{0,1600}')
    if (!$guard.Success -or $guard.Value -notmatch 'IS_MANUAL_INVINCIBLE\s*=\s*false') {
        $faults += 'DetectInteractables.cs no longer gives up the mod-made invulnerability when the player tries to interact, so the interact key does nothing after a level-up.'
    }
}

# 13. Nothing but the host's deliberate hold may stop the world during a session. A stopped world
#     is one a player can be stranded in, and holding a whole party on one player's level-up
#     choice is the single fault that has come back most often in this mod.
foreach ($file in $source) {
    if ($file.Name -eq 'CoopPause.cs' -or $file.Name -eq 'SpawnPlayerPortal.cs' -or $file.Name -eq 'BonkLinkSmoke.cs') { continue }
    $text = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $text.Count; $i++) {
        if ($text[$i] -match '^\s*//') { continue }
        if ($text[$i] -match 'MyTime\.Pause\s*\(\s*\)') {
            $faults += "$($file.Name):$($i + 1) stops the world mid-session. Slow it instead; see ChoiceSlowMotion."
        }
    }
}

# 14. Lobby health scaling must be applied exactly once. It was applied on the spawn path and
#     again on the stats path, so a party of three met nine times the health instead of three
#     and the difficulty never felt like the number of players.
# Counted only where scaling is actually applied -- the patches -- not where the multiplier is
# declared or worked out, which is the service.
$applications = 0
foreach ($file in ($source | Where-Object { $_.FullName -like '*\Patches\*' })) {
    $text = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $text.Count; $i++) {
        if ($text[$i] -match '^\s*(//|///)') { continue }
        if ($text[$i] -match 'GetEnemyHpMultiplier\s*\(') { $applications++ }
    }
}
if ($applications -ne 1) {
    $faults += "Lobby health scaling is applied $applications times in the plugin; it must be applied exactly once or the party faces the multiplier squared."
}

# 15. A level-up choice must not be reachable by the keyboard the moment it opens. A fresh window
#     selects its skip button, and space is submit, so the jump key threw the upgrade away. The
#     per-frame sweep cannot run before the frame the window opens on.
$keys = Join-Path $repo 'src/plugin/Patches/ChoiceWindowKeys.cs'
if (Test-Path -LiteralPath $keys) {
    $keysText = Get-Content -Raw -LiteralPath $keys
    if ($keysText -notmatch 'BaseEncounterWindow\.Open' -or $keysText -notmatch 'SetSelectedGameObject\(null\)') {
        $faults += 'ChoiceWindowKeys.cs no longer clears the selection as a choice opens, so the jump key can skip an upgrade on the frame it appears.'
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
