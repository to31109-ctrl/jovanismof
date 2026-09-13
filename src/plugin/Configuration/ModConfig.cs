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
        public static ConfigEntry<bool> EnabledSharedExperience { get; private set; }
        public static ConfigEntry<bool> CoopWorldSaves { get; private set; }
        public static ConfigEntry<bool> ResumeLastWorld { get; private set; }
        public static ConfigEntry<float> CoopAutosaveSeconds { get; private set; }
        public static ConfigEntry<int> CoopWorldsKept { get; private set; }
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
                false,
                "Allow game saves during netplay sessions."
            );
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
                30f,
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
