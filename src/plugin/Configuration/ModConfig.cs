// BonkLink edition changes, 2026-09-12: shared rewards on; upstream auto-update off.
// BonkLink edition, 2026-09-13: co-op world checkpoints and a stable installation identity.
using System;
using BepInEx.Configuration;

namespace MegabonkTogether.Configuration
{
    public static class ModConfig
    {
        private static ConfigFile configFile;

        // DEV_URL is "ws://127.0.0.1:5000"

        public static ConfigEntry<string> PlayerName { get; private set; }
        public static ConfigEntry<bool> CheckForUpdates { get; private set; }
        public static ConfigEntry<string> ServerUrl { get; private set; }
        public static ConfigEntry<uint> RDVServerPort { get; private set; }
        public static ConfigEntry<bool> ShowChangelog { get; private set; }
        public static ConfigEntry<string> PreviousVersion { get; private set; }
        public static ConfigEntry<bool> AllowSavesDuringNetplay { get; private set; }
        /// <summary>Records that the one-time repair below has already been applied.</summary>
        private static ConfigEntry<bool> KeptProgressRepairApplied;
        public static ConfigEntry<bool> EnabledSharedExperience { get; private set; }
        public static ConfigEntry<bool> CoopWorldSaves { get; private set; }
        public static ConfigEntry<bool> ResumeLastWorld { get; private set; }
        public static ConfigEntry<float> CoopAutosaveSeconds { get; private set; }
        public static ConfigEntry<int> CoopWorldsKept { get; private set; }
        public static ConfigEntry<float> ReviveGhostHealthPercent { get; private set; }
        public static ConfigEntry<bool> ReviveGhostFlies { get; private set; }
        public static ConfigEntry<bool> ReviveOnAreaBossDeath { get; private set; }
        public static ConfigEntry<bool> KeepLobbyOpenForRejoin { get; private set; }
        public static ConfigEntry<bool> ShareMyLogWithHost { get; private set; }
        public static ConfigEntry<float> ReviveHoldSeconds { get; private set; }
        public static ConfigEntry<bool> ReviveNeedsGhost { get; private set; }
        public static ConfigEntry<bool> ReviveOnNewArea { get; private set; }
        public static ConfigEntry<string> PlayerIdentity { get; private set; }
        public static ConfigEntry<string> UpdateRepository { get; private set; }
        public static ConfigEntry<bool> ShareUpdatesWithPeers { get; private set; }
        public static ConfigEntry<bool> AcceptUpdatesFromHost { get; private set; }

        public static void Initialize(ConfigFile config)
        {
            configFile = config;

            PlayerName = config.Bind(
                "Player",
                "PlayerName",
                "Player",
                "Your display name shown to other players. Please be respectful!"
            );
            CheckForUpdates = config.Bind(
                "Updates",
                "CheckForUpdates",
                true,
                "Check for a newer build on launch. Nothing is checked unless UpdateRepository names a repository, so this cannot pull in a different mod."
            );
            ServerUrl = config.Bind(
                "Network",
                "ServerUrl",
                "wss://megabonk-together-matchmaking.balatro-vs-matchmaking.eu",
                "The URL of the matchmaking server. Do not change this unless you know what you're doing (e.g. for self-hosting). Use ws://127.0.0.1:5000 on localhost for testing purpose"
            );
            RDVServerPort = config.Bind(
                "Network",
                "RDVServerPort",
                (uint)5678,
                "The port of the relay server. Do not change this unless you know what you're doing"
            );
            ShowChangelog = config.Bind(
                "Updates",
                "ShowChangelog",
                false,
                "Internal flag to show changelog after an update. Do not modify manually."
            );
            PreviousVersion = config.Bind(
                "Updates",
                "PreviousVersion",
                "",
                "Internal flag to store the previous version before an update. Do not modify manually."
            );
            AllowSavesDuringNetplay = config.Bind(
                "Gameplay",
                "AllowSavesDuringNetplay",
                true,
                "Let the game keep its own progress while playing co-op: characters you unlock, quests, stats and silver. Switched off, none of that is written while you are in a session, so anything unlocked playing together is gone the next time the game starts. It was off by default to keep co-op from touching single-player progression, which cost players the characters they had earned."
            );

            // Changing the default above reaches nobody who already has the mod: their config
            // file already says false, and an update only replaces the plugin -- it never runs
            // the installer that would repair the file. Left alone, everyone who had already
            // installed kept losing every character they unlocked in co-op.
            //
            // Done once, and recorded, so anyone who deliberately turns it off again keeps it off.
            KeptProgressRepairApplied = config.Bind(
                "Internal",
                "KeptProgressRepairApplied",
                false,
                "Internal. Records that saving during co-op was switched on once, for installations that predate it. Do not modify manually."
            );

            if (!KeptProgressRepairApplied.Value)
            {
                if (!AllowSavesDuringNetplay.Value)
                {
                    AllowSavesDuringNetplay.Value = true;
                    Plugin.Log.LogInfo("Switched on keeping the game's own progress during co-op; unlocks earned together were being thrown away.");
                }
                KeptProgressRepairApplied.Value = true;
            }
            EnabledSharedExperience = config.Bind(
                "Gameplay",
                "EnabledSharedExperience",
                true,
                "Enable Host experience (Same XP and pause enabled). Disable for no pause and separate XP."
            );
            CoopWorldSaves = config.Bind(
                "CoopSaves",
                "CoopWorldSaves",
                true,
                "Save the co-op world (players, inventories, health, enemies, boss health and run progress) while hosting. Written to BepInEx/BonkLinkWorlds; your normal single-player progression save is never touched."
            );
            ResumeLastWorld = config.Bind(
                "CoopSaves",
                "ResumeLastWorld",
                true,
                "When hosting a run on the same stage as your newest checkpoint, restore it instead of starting fresh."
            );
            CoopAutosaveSeconds = config.Bind(
                "CoopSaves",
                "CoopAutosaveSeconds",
                60f,
                new ConfigDescription("Seconds between automatic checkpoints. Checkpoints are also written on stage changes, boss deaths and when the session ends.", new AcceptableValueRange<float>(5f, 600f))
            );
            CoopWorldsKept = config.Bind(
                "CoopSaves",
                "CoopWorldsKept",
                5,
                new ConfigDescription("How many co-op worlds to keep on disk before the oldest is deleted.", new AcceptableValueRange<int>(1, 50))
            );
            UpdateRepository = config.Bind(
                "Updates",
                "UpdateRepository",
                "",
                "GitHub repository to update this mod from, as owner/repo. Leave empty to never update. Point it at your own fork's repository so everyone you play with receives your builds; pointing it at somebody else's replaces this mod with theirs."
            );
            ShareUpdatesWithPeers = config.Bind(
                "Updates",
                "ShareUpdatesWithPeers",
                false,
                "Unfinished and switched off: the receiving half is not verified. While hosting a private room, hand your build to anyone who joins on an older one, so a group stays on the same version without sending files around."
            );
            AcceptUpdatesFromHost = config.Bind(
                "Updates",
                "AcceptUpdatesFromHost",
                false,
                "Unfinished and switched off: not verified. Accept a newer build from the host of a private room. This installs program files sent by whoever is hosting, so only leave it on for people you would already accept a download from. Public rooms are never accepted."
            );
            PlayerIdentity = config.Bind(
                "Player",
                "PlayerIdentity",
                "",
                "Internal, stable id for this installation so a host can give you your own character back when you rejoin. Do not share or edit."
            );

            ReviveGhostHealthPercent = config.Bind(
                "Gameplay",
                "ReviveGhostHealthPercent",
                25f,
                new ConfigDescription("How tough the ghost that must be killed to revive a downed player is, as a percentage of the enemy it is built from. It is spawned as a boss, so at 100 percent it also carries boss and player-count health scaling and takes a very long time to kill.", new AcceptableValueRange<float>(1f, 200f))
            );
            ReviveGhostFlies = config.Bind(
                "Gameplay",
                "ReviveGhostFlies",
                false,
                "Let the revive ghost fly. It is built from a flying enemy, and left flying it drifts up and away from the players trying to kill it."
            );
            ShareMyLogWithHost = config.Bind(
                "Gameplay",
                "ShareMyLogWithHost",
                true,
                "When the host of a private room saves the world, send them the end of your JOVANISMOF log so a fault can be read from both sides instead of guessed at. Only this mod's own log is ever sent, only its last part, and only to the host of a room you are already in. Set to false to send nothing."
            );
            KeepLobbyOpenForRejoin = config.Bind(
                "Gameplay",
                "KeepLobbyOpenForRejoin",
                true,
                "Leave a private Friendlies room joinable after the run has started, so somebody who closed the game can come back into it with the room code. Telling the matchmaking server the game had begun is what made it refuse them. Only affects private rooms, which already need the code to enter."
            );
            ReviveOnAreaBossDeath = config.Bind(
                "Gameplay",
                "ReviveOnAreaBossDeath",
                true,
                "Killing the area boss brings back everyone who is down, as a second way out when the ghost cannot be reached."
            );

            ReviveHoldSeconds = config.Bind(
                "Gameplay",
                "ReviveHoldSeconds",
                5f,
                new ConfigDescription("Seconds the interact key must be held at a fallen player's coffin to start reviving them. Zero revives on the press, as it used to.", new AcceptableValueRange<float>(0f, 15f))
            );

            ReviveNeedsGhost = config.Bind(
                "Gameplay",
                "ReviveNeedsGhost",
                false,
                "Bring back the old revive, where a ghost had to be killed before the fallen player returned. Off by default: it could fail to spawn on a full map and it drifted away from the people fighting it."
            );
            ReviveOnNewArea = config.Bind(
                "Gameplay",
                "ReviveOnNewArea",
                true,
                "Everyone who is down comes back when the party reaches a new area."
            );

            if (!Guid.TryParse(PlayerIdentity.Value, out _))
            {
                PlayerIdentity.Value = Guid.NewGuid().ToString("N");
                config.Save();
            }
        }

        public static void Save()
        {
            configFile?.Save();
        }
    }
}
