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

# 16. A recycled enemy must not be re-registered on the minimap while its old entry is still
#     there. The game adds to that dictionary with Add, which throws, and the throw happens
#     inside InitEnemy -- so the enemy is left half set up, active and never finished. This is
#     the most likely cause found for enemies that drift or behave as if they were never given
#     their state.
$minimap = Join-Path $repo 'src/plugin/Patches/MinimapCamera.cs'
if (!(Test-Path -LiteralPath $minimap)) {
    $faults += 'src/plugin/Patches/MinimapCamera.cs is gone, so a recycled enemy can throw halfway through InitEnemy again.'
} else {
    $minimapText = Get-Content -Raw -LiteralPath $minimap
    if ($minimapText -notmatch 'MinimapCamera\.OnEnemySpawn' -or $minimapText -notmatch 'ContainsKey') {
        $faults += 'MinimapCamera.cs no longer clears a stale icon before an enemy is set up again, so InitEnemy can throw partway through.'
    }
}

# 17. A checkpoint must record the stage the party is standing in. It used to record
#     runConfig.stageData, which never changes after the run begins, so every save of every run
#     claimed to be on the map's first stage -- and loading one really did put everyone back at
#     the beginning, because that is what the file said.
$capture = Join-Path $repo 'src/plugin/Persistence/WorldCapture.cs'
if (Test-Path -LiteralPath $capture) {
    # Code only. The comment above this line in WorldCapture.cs names currentStage to explain
    # what went wrong, and matching that would make this rule pass without the fix present.
    $captureCode = Get-Content -LiteralPath $capture | Where-Object { $_ -notmatch '^\s*(//|///)' }
    if (($captureCode -join "`n") -notmatch 'MapController\.currentStage') {
        $faults += 'WorldCapture.cs no longer records MapController.currentStage, so every checkpoint claims to be on the first stage of the map.'
    }
}

# 18. A portal takes the whole party. Waiting for every peer to report ready left a player behind
#     in the previous area, still fighting a boss the others had already killed.
$map = Join-Path $repo 'src/plugin/Patches/MapController.cs'
if (Test-Path -LiteralPath $map) {
    $mapLines = Get-Content -LiteralPath $map
    for ($i = 0; $i -lt $mapLines.Count; $i++) {
        if ($mapLines[$i] -match '^\s*(//|///)') { continue }
        if ($mapLines[$i] -match 'AreAllPeersReady\s*\(') {
            $faults += "MapController.cs:$($i + 1) makes a stage change wait for every peer to be ready, which leaves players behind in the old area."
        }
    }
}

# 19. The game's own progress is always saved. Making it a setting meant a player whose setting
#     was off lost every character and unlock they earned in co-op, silently and for ever.
$saves = Join-Path $repo 'src/plugin/Patches/SaveManager.cs'
if (Test-Path -LiteralPath $saves) {
    $savesText = Get-Content -Raw -LiteralPath $saves
    if ($savesText -match 'AllowSavesDuringNetplay') {
        $faults += 'SaveManager.cs gates the game''s own saving on a setting again; a player with it off loses every unlock earned in co-op.'
    }
}

# 20. A projectile from a weapon this machine has never held must not be able to throw on its
#     way back to the pool. The pools are keyed by weapon, another player's build brings weapons
#     this player does not have, and the throw happens after the projectile stops being used and
#     before it is put away -- so it is never released and never hidden. Two clients logged about
#     a hundred of these each, stack traces were 62% of everything they wrote, and because BepInEx
#     writes to disk on the main thread both sat at nine frames a second with their CPU and GPU
#     at twenty percent. They were not working; they were waiting on a file.
$pool = Join-Path $repo 'src/plugin/Patches/PoolManager.cs'
if (Test-Path -LiteralPath $pool) {
    $poolText = Get-Content -Raw -LiteralPath $pool
    if ($poolText -notmatch 'HarmonyFinalizer' -or $poolText -notmatch 'ReturnProjectile') {
        $faults += 'PoolManager.cs no longer catches a projectile the game cannot pool. Each one leaks and writes a stack trace to disk from the main thread, which is what took two clients to nine frames a second.'
    }
}

# 21. Nothing may spawn past what the game actually allocated for enemies. Overrunning the pool
#     hands back an enemy that is still alive and in play, which is then torn out of the fight
#     and rebuilt elsewhere -- what players describe as mobs flying around the map. Both things
#     allowed past the crowd limit, a boss and the revive ghost, must still respect the ceiling.
$enemyMgr = Join-Path $repo 'src/plugin/Patches/Enemies/EnemyManager.cs'
if (Test-Path -LiteralPath $enemyMgr) {
    $enemyCode = (Get-Content -LiteralPath $enemyMgr | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($enemyCode -notmatch 'GetPooledEnemyCeiling') {
        $faults += 'EnemyManager.cs lets a boss or a revive ghost spawn without checking the enemy pool ceiling, which recycles enemies that are still alive.'
    }
    if ($enemyCode -match 'CurrentReviver\.HasValue[\s\S]{0,80}return true;') {
        $faults += 'EnemyManager.cs waves through every spawn while a revive coffin exists, so one downed player switches the enemy limit off for the whole party.'
    }
}

# 22. The mod must not read back its own invented numbers. GetNumMaxEnemies is replaced with 1000
#     during a session to keep enemies aggressive, and the balance service was asking that same
#     method what the game allows for one player.
$balance = Join-Path $repo 'src/plugin/Services/GameBalanceService.cs'
if (Test-Path -LiteralPath $balance) {
    $balanceCode = (Get-Content -LiteralPath $balance | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($balanceCode -match 'GetNumMaxEnemies' -and $balanceCode -notmatch 'AskingTheGameDirectly') {
        $faults += 'GameBalanceService.cs asks GetNumMaxEnemies without silencing the mod''s own override, so it reads back a number the mod invented.'
    }
}

# 23. Loading a world must give everyone their run back. A checkpoint records who was connected
#     when it was written, and a world saved mid-run marks everyone connected -- so on loading it
#     OnClientReady decided every player was "already in this run" and restored nobody. Everyone
#     started blank and re-picked every upgrade and item, which is the opposite of loading a save.
# 24. And a checkpoint may never replace a better one with an emptier one. A capture taken while
#     a player's inventory is being rebuilt records them with nothing, and writing that over a
#     good checkpoint quietly destroys the world.
$worldSvc = Join-Path $repo 'src/plugin/Services/WorldSaveService.cs'
if (Test-Path -LiteralPath $worldSvc) {
    $worldCode = (Get-Content -LiteralPath $worldSvc | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    # Matched on the resume loop and on the call site, not on bare names: a previous version of
    # this rule matched the helper's own definition and passed against a file with the fix cut out.
    if ($worldCode -notmatch 'foreach\s*\(var slot in resume\.Players\)[\s\S]{0,200}Connected\s*=\s*false') {
        $faults += 'WorldSaveService.cs no longer clears Connected on a world being continued, so loading a save restores nobody and everyone re-picks their run.'
    }
    if ($worldCode -notmatch 'WhoWasCapturedEmpty\(save') {
        $faults += 'WorldSaveService.cs no longer refuses a checkpoint that captured a player empty, so a snapshot taken mid-transition can overwrite a good world with a hollow one.'
    }
}

# 25. The number of mobs is the game's own and is not a setting. Raising it above what the game
#     expects put weaker machines at nine frames a second, and a machine that slow falls behind
#     everyone else's world. Difficulty comes from health, which scales with the party.
$menuFile = Join-Path $repo 'src/plugin/Scripts/Modal/NetworkMenuTab.cs'
if (Test-Path -LiteralPath $menuFile) {
    $menuCode = (Get-Content -LiteralPath $menuFile | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($menuCode -match 'scalingDraft\.EnemyCap') {
        $faults += 'NetworkMenuTab.cs offers the mob limit as a setting again. It is the game''s own number; raising it is what took weaker machines to nine frames a second.'
    }
}
$scalingFile = Join-Path $repo 'src/common/Models/LobbyScaling.cs'
if (Test-Path -LiteralPath $scalingFile) {
    $scalingCode = (Get-Content -LiteralPath $scalingFile | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($scalingCode -notmatch 'EnemyCap\s*\{\s*get;\s*set;\s*\}\s*=\s*AutomaticEnemyCap') {
        $faults += 'LobbyScaling.cs no longer defaults the mob limit to the game''s own number.'
    }
}

# 26. Another player's projectile must come from the game's pool, not from Instantiate. Built
#     fresh, every shot from every other player was one object created and one destroyed on the
#     main thread, and that cost grew with the number of players and how much they fired --
#     "the more players, the less FPS" and "the more attacks, the worse it gets".
$syncFile = Join-Path $repo 'src/plugin/Services/SynchronizationService.cs'
if (Test-Path -LiteralPath $syncFile) {
    $syncCode = (Get-Content -LiteralPath $syncFile | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($syncCode -match 'var proj = GameObject\.Instantiate\(attack\.prefabProjectile\)') {
        $faults += 'SynchronizationService.cs builds other players'' projectiles with Instantiate again instead of taking them from the pool, which costs a frame budget that grows with every player firing.'
    }
    if ($syncCode -notmatch 'TakeProjectileFromPool\(attack\)') {
        $faults += 'SynchronizationService.cs no longer takes other players'' projectiles from the game''s pool.'
    }
}

# 27. Transient state -- positions of players and projectiles -- is never sent reliably. Sent
#     ReliableOrdered, a lost packet holds up every packet behind it and a machine that falls
#     behind must replay every stale position in order; it can never skip to the present. That is
#     what "they are in the past" is. The old code switched to ReliableOrdered whenever a message
#     outgrew a datagram, which with more than two players was every tick.
$udpFile = Join-Path $repo 'src/plugin/Services/UdpClientService.cs'
if (Test-Path -LiteralPath $udpFile) {
    $udpCode = (Get-Content -LiteralPath $udpFile | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    # Only the four per-tick state broadcasts. SendToHost<T> carries one-off messages that may
    # legitimately be large and reliable (a returning player's state, a log tail), and must not
    # be caught here.
    foreach ($stream in @('SendLobbyUpdate', 'SendEnemiesUpdate', 'SendProjectilesUpdate', 'SendTumbleWeedsUpdate')) {
        $body = [regex]::Match($udpCode, "private void $stream\(\)[\s\S]*?(?=
        (public|private|internal) )")
        if (!$body.Success) { $faults += "UdpClientService.cs no longer has $stream(); the per-tick stream it sent has gone somewhere unchecked."; continue }
        if ($body.Value -match 'ReliableOrdered') {
            $faults += "UdpClientService.cs $stream() sends a per-tick state stream ReliableOrdered; split it into small unreliable packets instead."
        }
    }
    foreach ($fn in @('TransientPackets.EncodePlayers', 'TransientPackets.EncodeProjectiles', 'TransientPackets.EncodeTumbleWeeds', 'EnemyPackets.Encode')) {
        if ($udpCode -notmatch ([regex]::Escape($fn) + '\(')) {
            $faults += "UdpClientService.cs no longer sends via $fn, so that stream can grow past a datagram and go reliable."
        }
    }
}

# 28. Another player's muzzle flash is reused, not built per shot. Built per shot and never
#     destroyed, a revolver firing for a minute left hundreds of them in the scene for the run.
$syncFile2 = Join-Path $repo 'src/plugin/Services/SynchronizationService.cs'
if (Test-Path -LiteralPath $syncFile2) {
    $syncCode2 = (Get-Content -LiteralPath $syncFile2 | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    $muzzleBuilds = ([regex]::Matches($syncCode2, 'Instantiate\(attack\.prefabMuzzle\)')).Count
    if ($muzzleBuilds -ne 1) {
        $faults += "SynchronizationService.cs builds another player's muzzle flash in $muzzleBuilds places; it must be built once inside PlayForeignMuzzle and reused."
    }
    if ($syncCode2 -notmatch 'PlayForeignMuzzle\(projectile\.OwnerId') {
        $faults += 'SynchronizationService.cs no longer routes muzzle flashes through PlayForeignMuzzle.'
    }
}

# 29. Stat upgrades stay out of the lobby broadcast. Only the host needs them and they have
#     their own message; inside every lobby packet at thirty a second they were about a
#     kilobyte per player and pushed the broadcast onto the reliable path.
$playerModel = Join-Path $repo 'src/common/Models/Player.cs'
if (Test-Path -LiteralPath $playerModel) {
    $playerCode = (Get-Content -LiteralPath $playerModel | Where-Object { $_ -notmatch '^\s*(//|///)' }) -join "`n"
    if ($playerCode -notmatch 'MemoryPackIgnore\]\s*public List<Persistence\.SavedModifier> Stats') {
        $faults += 'Player.cs sends Stats inside the lobby broadcast again; that is a kilobyte per player thirty times a second.'
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
