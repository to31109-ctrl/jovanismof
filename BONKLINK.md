# BonkLink edition — development build

This is a modified Megabonk Together 5.1.0, based on upstream commit
009e4bac2731364cbcebe8324f1fbce34c528806:
https://github.com/Fcornaire/megabonk-together

The original authors retain their copyright. This edition and its changes are
distributed under GPL-2.0; see LICENSE. Changes were made on 12–13 September 2026.
BonkWithFriends was inspected as a reference; its DLL and decompiled code are
not part of this distribution.

## Intended experience

Private room codes for 2–5 players, using LiteNetLib networking, automatic NAT
traversal and the upstream public relay when direct connections fail. No Steam
networking API or manual port forwarding is used by the mod. Each player needs
their own compatible game installation and internet access to the public service.

The upstream gameplay implementation synchronizes players, enemies, bosses,
damage, pickups, interactables and run transitions. Shared experience is enabled
by default and also shares gold earned; purchases still spend each player's own
balance. Reviving uses the upstream coffin/ghost encounter. Writing to the game's
own single-player progression save stays disabled during netplay.

## Co-op world saves

The host keeps its own checkpoints of the shared run, in
`BepInEx/BonkLinkWorlds/<world>.bonkworld`. These files are separate from the
game's progression save, which the mod never writes during netplay.

A checkpoint is written when a run starts, every 30 seconds, when a stage is
finished, and when the session ends. Each file is checksummed and replaced
atomically, and the previous checkpoint is retained, so an interrupted write
cannot lose the run. When the run is lost or completed the checkpoint is deleted,
so a finished run is never offered again.

Each checkpoint holds:

- the run itself: map, stage, tier, challenge, music track, seed and elapsed time;
- every player: name, character, position, health, maximum health, shield and
  overheal, gold, experience, level, banishes, refreshes and skips, every weapon
  with the exact modifiers of each level it was upgraded with, every tome with
  its level, and every item with its count;
- every live enemy: species, flags, wave, position, current and maximum health,
  and armour — bosses and the final boss included;
- run progress flags: boss curses, crypt and dungeon state, and boss orbs;
- the world objects still standing, and every chest, shrine and interactable the
  players already used up.

Two things use it:

- **Resuming a world.** With `ResumeLastWorld` on, hosting the same stage again
  restores the newest checkpoint for it: the enemies respawn where they were with
  the health they had, and every player gets their own character back. Each peer
  applies its own state from the same records, so all screens agree.
- **Rejoining.** Every installation has a stable `PlayerIdentity` in its config.
  A player who drops out and comes back to the same session is matched on that id
  and handed their own inventory, health and gold rather than a fresh character.

Settings live under `[CoopSaves]`: `CoopWorldSaves`, `ResumeLastWorld`,
`CoopAutosaveSeconds` and `CoopWorldsKept`.

A resumed stage is rebuilt from its seed, so its chests and interactables are
generated again. Each one that the checkpoint records as already used is dropped
as it appears, before any player is told it exists, matched on what it is and
where it stands. Nothing is dropped unless it matches, so a stage that generates
differently simply comes back whole rather than half-empty.

What a checkpoint does not carry, because the game does not keep it in a form
that can be replayed:

- pickups lying on the ground;
- enemy debuffs, special attack timers, charm and teleport state;
- projectiles in flight;
- tome rarity and the per-level history of tome modifiers: the game keeps only the
  accumulated total, which is what gets restored;
- boss orb health.

Every checkpoint logs exactly which of these it could not capture, and a
checkpoint whose run configuration or players could not be read is never offered
for resume.

## Changes in this edition

- Reassemble fragmented WebSocket messages; reject oversized/non-binary frames.
- Serialize concurrent WebSocket sends and bound their duration.
- Escape names and private room codes in matchmaking requests.
- Send at most one current snapshot per update after a frame hitch.
- Player snapshots at 30 Hz and enemy snapshots at 20 Hz, pending gameplay tuning.
- Correct a nullable match-state guard.
- Resume asynchronous game callbacks on an explicit game-thread context.
- Serialize simultaneous lobby joins and poll network events only on the game thread.
- Complete socket teardown before reconnecting and ignore stale session callbacks.
- Prevent clients from echoing received item damage back to the host.
- Compare enemy movement against the last sent state and refresh state every second.
- Split enemy/orb snapshots into small datagrams instead of reliable fragmented queues.
- Bound UDP port binding attempts to prevent an endless startup loop.
- Restore message suppression even when applying received gold throws.
- Enable shared rewards by default.
- Disable upstream auto-updates, including checks on menu entry, to retain this fork.
- Add isolated-save runtime test harness and protocol/scheduling/threading checks.
- Add co-op world checkpoints: players, inventories, health, enemies, boss health
  and run progress, with world resume and rejoin restore.
- Carry the run seed with the run so a resumed world rebuilds the same stage.
- Report each player's own gold, experience and level, so a checkpoint records the
  other players truthfully instead of reading the host's display-only mirror.
- Ignore a hidden player model's position rather than checkpointing or restoring it.
- Stop enemy retargeting from throwing when the last living player dies.
- Turn off the BepInEx console window for players; logs still go to LogOutput.log.
- Keep the host in their run when the last client leaves, instead of ejecting them.
- Close a player's pause or settings screen before a shared reward pauses the world.
- Rebuild a remote player's inventory when they change character.
- Add a Continue Saved Co-op World toggle and a Save Co-op World button in the pause menu.
- Add a world picker on the Friendlies screen: New World, or continue a saved world.
- Remember every player and every character that has played a world, keyed by installation
  identity and character, so a returning player gets that character back and a newcomer,
  or a character not played there before, starts fresh.
- Show the mod as JOVANISMOF, keeping the plugin GUID and assembly name so existing
  configurations, player identities and saved worlds continue to match.
- Point the auto-updater at a configured repository instead of upstream, so a fork updates
  its own players; with no repository configured it never checks at all.
- Show a splash window while BepInEx prepares itself on the first launch.
- Make New World mean a new world: nothing from any saved world is restored, and a fresh run
  no longer inherits the previous world roster, its levels or its gold.
- Restore a returning player at most once per session, so a stage change never rewinds
  anyone to an earlier checkpoint.
- Scale lobby difficulty gently by default, since mob count and mob health multiply.
- Apply the host lobby scaling to enemy and boss health, which the menu offered but nothing read.
- Use the IL2CPP-safe component lookups when building the pause menu button.

## Building

Install .NET SDK 9 and BepInEx IL2CPP build 752 into a separate game copy.
Launch that copy once to generate BepInEx/interop. Then run:

```powershell
dotnet build src/plugin/MegabonkTogether.Plugin.csproj -c Release -p:CI=true -p:MegabonkPath="C:\Games\MegabonkTest"
dotnet run --project tests/BonkLink.Checks -c Release
```

Copy the output from src/plugin/bin/Release/net6.0 to the test installation's
BepInEx/plugins/MegabonkTogether folder. Game assemblies are deliberately not
included in the source distribution; the build uses your generated assemblies.

Adding `-p:DefineConstants=BONKLINK_TESTING` produces an automated test build with
isolated saves and automatic exit. **Do not distribute that build to players.**
The optional checks argument `--public-relay` opens a short-lived private test
room with five WebSocket connections. It does not validate gameplay or UDP relay
traffic. Cross-country latency and connectivity require real remote testing.
