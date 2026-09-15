// BonkLink edition checks, 2026-09-12. GPL-2.0; see LICENSE.
using System.Net.WebSockets;
using MegabonkTogether.Common.Networking;
using MegabonkTogether.Common.Messages;
using MegabonkTogether.Common.Messages.WsMessages;
using MemoryPack;
using MegabonkTogether.Common.Models;
using MegabonkTogether.Common.Persistence;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
async Task Reject<T>(Func<Task> action, string name) where T:Exception
{ try { await action(); } catch(T) { Check(true,name); return; } throw new Exception("Expected rejection: " + name); }
void Refuses(Action action, string name)
{ try { action(); } catch(InvalidDataException) { Check(true,name); return; } throw new Exception("Expected rejection: " + name); }
WorldSave Broken(WorldSave save, Action<WorldSave> damage) { damage(save); return save; }
float clock = 0;
var rateProbe = .35f;
for (var i = 0; i < 13; i++) rateProbe = LobbyScaling.StepRate(rateProbe, 1);
Check(rateProbe == 1f, "35 percent health setting can reach exactly 100 percent");
Check(LobbyScaling.StepRate(1.1f, -1) == 1.05f && LobbyScaling.StepRate(1.05f, -1) == 1f, "110 percent setting steps down to exactly 100 percent");
Check(LobbyScaling.StepRate(0, -1) == 0 && LobbyScaling.StepRate(3, 1) == 3, "percentage controls stay within bounds");
for (var count = 1; count <= 5; count++)
    Check(LobbyScaling.Multiplier(count, 1f) == count, $"default health and spawn scaling is {count}x for {count} players");
Check(LobbyScaling.Multiplier(5, 0f) == 1f, "host can disable player scaling");
Check(LobbyScaling.Multiplier(3, .5f) == 2f, "custom 50 percent scaling gives 2x for three players");
Check(!new LobbyScaling { SpawnsPerPlayer = float.NaN }.IsValid(), "invalid lobby scaling is rejected");
// A party now faces the number of mobs a single player does. Adding mobs per player multiplied
// with the health scaling and with the pool the game allocates, and was asked to be removed.
var defaults = new LobbyScaling();
Check(defaults.SpawnsPerPlayer == 0f, "a party does not get extra mobs for having more players");
// Without a log file nothing a player reports can be looked into from their own machine, and
// only the installer ever switched this on -- which an update never runs.
string Cfg(params string[] parts) => string.Join(Environment.NewLine, parts) + Environment.NewLine;
var loggingOff = Cfg("[Logging.Disk]", "Enabled = false");
var noDiskSection = Cfg("[Logging.Console]", "Enabled = false");
var alreadyOn = Cfg("[Logging.Console]", "Enabled = false", "", "[Logging.Disk]", "Enabled = true");
var consoleThenDisk = Cfg("[Logging.Console]", "Enabled = false", "", "[Logging.Disk]", "Enabled = false");
Check(MegabonkTogether.Common.LoggingConfig.WithDiskLoggingOn(loggingOff).Contains("Enabled = true"), "disk logging is switched on when it was off");
Check(MegabonkTogether.Common.LoggingConfig.WithDiskLoggingOn(noDiskSection).Contains("[Logging.Disk]"), "a missing disk logging section is added");
Check(MegabonkTogether.Common.LoggingConfig.WithDiskLoggingOn(alreadyOn).Contains("[Logging.Disk]"), "an already correct file still has its disk section");
Check(MegabonkTogether.Common.LoggingConfig.WithDiskLoggingOn(consoleThenDisk).Contains("[Logging.Console]"), "the console section is left alone");
Check(LobbyScaling.Multiplier(5, defaults.SpawnsPerPlayer) == 1f, "five players face a single player's mob count");
// Health is what scales with the party instead: two players, twice the health.
Check(LobbyScaling.Multiplier(2, defaults.EnemyHealthPerPlayer) == 2f, "two players give enemies twice the health");
Check(LobbyScaling.Multiplier(3, defaults.EnemyHealthPerPlayer) == 3f, "three players give enemies three times the health");
Check(LobbyScaling.Multiplier(2, defaults.BossHealthPerPlayer) == 2f, "two players give a boss twice the health");
Check(LobbyScaling.Multiplier(1, defaults.EnemyHealthPerPlayer) == 1f, "one player changes nothing");
// The game reuses enemies from a fixed pool. Going past it makes it recycle enemies that are
// still alive, which is enemies teleporting around the map and bosses with nothing to spawn
// from. Nothing the host can set may exceed what the game actually allocated.
var pooled = new LobbyScaling { EnemyCap = LobbyScaling.AutomaticEnemyCap };
Check(pooled.ResolveEnemyCap(5, 1500, 800) <= 800 - 25, "automatic scaling never exceeds the enemy pool");
Check(new LobbyScaling { EnemyCap = 2500 }.ResolveEnemyCap(1, 1500, 800) <= 800 - 25, "a hand-set cap never exceeds the enemy pool either");
Check(pooled.ResolveEnemyCap(2, 500, 4000) == 1000, "two players get twice one player's mobs when the pool allows it");
Check(pooled.ResolveEnemyCap(1, 500, 4000) == 500, "one player gets the game's own limit");
Check(pooled.ResolveEnemyCap(3, 0, 0) >= 100, "an unknown limit still leaves a usable cap");
Check(pooled.ResolveEnemyCap(5, 1500, 110) >= 100, "a tiny pool still leaves room to play");

// Continuing a world. A run always begins on a map's first stage, so a world saved further in
// could never be continued and the host silently got a new one instead.
var forest = new[] { "StageForest1", "StageForest2", "StageForest3" };
Check(WorldResume.Decide(1, "StageForest2", 1, "StageForest1", forest) == ResumeDecision.SwitchStage,
      "a world saved on a later stage starts that stage instead of a new run");
Check(WorldResume.Decide(1, "StageForest1", 1, "StageForest1", forest) == ResumeDecision.AlreadyAligned,
      "a world saved on the first stage needs no change");
Check(WorldResume.Decide(2, "StageDesert1", 1, "StageForest1", forest) == ResumeDecision.WrongMap,
      "a world from another map is not silently loaded into this one");
Check(WorldResume.Decide(1, "StageForest9", 1, "StageForest1", forest) == ResumeDecision.StageMissing,
      "a world saved on a stage this map no longer has is refused");
Check(WorldResume.Decide(null, "", 1, "StageForest1", forest) == ResumeDecision.StartNew,
      "choosing no world starts a new run");
Check(WorldResume.Explain(ResumeDecision.WrongMap, "Desert - StageDesert1", "StageDesert1").Length > 0,
      "a host who cannot continue a world is told why");
Check(WorldResume.Explain(ResumeDecision.AlreadyAligned, "x", "y").Length == 0,
      "nothing is said when the world loads normally");
var rulesMessage = new RunStarted { Scaling = new LobbyScaling { EnemyHealthPerPlayer = .5f, BossHealthPerPlayer = 2f, SpawnsPerPlayer = .25f, EnemyCap = 900 } };
var receivedRules = MemoryPackSerializer.Deserialize<RunStarted>(MemoryPackSerializer.Serialize(rulesMessage));
Check(receivedRules!.Scaling.EnemyCap == 900 && receivedRules.Scaling.BossHealthPerPlayer == 2f && receivedRules.Scaling.SpawnsPerPlayer == .25f, "host scaling survives the run-start network message");
var gameContext = new GameThreadContext();
var ownerThread = Environment.CurrentManagedThreadId;
var restoredContext = SynchronizationContext.Current;
var visitedThreads = new List<int>();
var dispatched = gameContext.RunAsync(async () =>
{
    visitedThreads.Add(Environment.CurrentManagedThreadId);
    await Task.Delay(10);
    visitedThreads.Add(Environment.CurrentManagedThreadId);
    await Task.Run(() => Thread.Sleep(5));
    visitedThreads.Add(Environment.CurrentManagedThreadId);
});
var deadline = System.Diagnostics.Stopwatch.StartNew();
while (!dispatched.IsCompleted && deadline.ElapsedMilliseconds < 2000) { gameContext.DrainOne(); Thread.Sleep(1); }
Check(dispatched.IsCompletedSuccessfully && visitedThreads.Count==3 && visitedThreads.All(t=>t==ownerThread),"async networking continuations return to owning game thread");
Check(ReferenceEquals(restoredContext,SynchronizationContext.Current),"game dispatch restores previous synchronization context");
var foreignDrainRejected = Task.Run(() => { try { gameContext.DrainOne(); return false; } catch(InvalidOperationException) { return true; } }).GetAwaiter().GetResult();
Check(foreignDrainRejected,"background thread cannot dispatch game callbacks");
Check(!SnapshotSchedule.Due(ref clock,.01f,.05f), "wait for snapshot interval");
Check(SnapshotSchedule.Due(ref clock,.045f,.05f), "snapshot becomes due");
Check(SnapshotSchedule.Due(ref clock,2f,.05f), "hitch sends one current snapshot");
Check(!SnapshotSchedule.Due(ref clock,0,.05f), "hitch does not leave duplicate backlog");
Check(!SnapshotSchedule.Due(ref clock,float.NaN,.05f) && float.IsFinite(clock), "invalid time rejected");
var payload = Enumerable.Range(0,12000).Select(i=>(byte)i).ToArray();
var wave = Enumerable.Range(0,1500).Select(i => new EnemyModel { Id=(uint)i, Hp=10000 }).ToArray();
var orbs = Enumerable.Range(0,100).Select(i => new BossOrbModel { Id=(uint)i }).ToArray();
var packets = EnemyPackets.Encode(wave,orbs).ToArray();
Check(packets.All(p => p.Length < 900),"1500-enemy wave stays within datagram budget");
var decoded = packets.Select(p=>(LobbyUpdates)MemoryPackSerializer.Deserialize<IGameNetworkMessage>(p)!).ToArray();
Check(decoded.SelectMany(p=>p.Enemies).Select(e=>e.Id).SequenceEqual(wave.Select(e=>e.Id)),"batched enemy snapshots preserve every enemy exactly once");
Check(decoded.SelectMany(p=>p.BossOrbs).Select(e=>e.Id).SequenceEqual(orbs.Select(e=>e.Id)),"batched boss orbs preserve every orb exactly once");
var baseline = new SnapshotBaseline<int,float>();
bool Moved(float a,float b) => Math.Abs(a-b) > .1f;
Check(baseline.Collect(new Dictionary<int,float>{{1,0}},Moved,false).Count==1,"new enemy sends state");
Check(baseline.Collect(new Dictionary<int,float>{{1,.06f}},Moved,false).Count==0,"tiny movement held");
Check(baseline.Collect(new Dictionary<int,float>{{1,.12f}},Moved,false).Count==1,"small movements accumulate against sent state");
Check(baseline.Collect(new Dictionary<int,float>{{1,.12f}},Moved,true).Count==1,"refresh repairs a lost final update");
baseline.Collect(new Dictionary<int,float>(),Moved,false);
Check(baseline.Collect(new Dictionary<int,float>{{1,.12f}},Moved,false).Count==1,"removed entity does not retain stale baseline");
using(var stream = new FakeSocket(payload,777)) Check((await SocketFrames.ReadBinary(stream,default)).SequenceEqual(payload), "reassemble large fragmented binary message");
await Reject<InvalidDataException>(()=>SocketFrames.ReadBinary(new FakeSocket(payload,1000),default,5000), "oversized message rejected");
await Reject<InvalidDataException>(()=>SocketFrames.ReadBinary(new FakeSocket([1],1,WebSocketMessageType.Text),default), "text message rejected");
await Reject<EndOfStreamException>(()=>SocketFrames.ReadBinary(new FakeSocket([],1,WebSocketMessageType.Close),default), "closed connection rejected");
using(var cts=new CancellationTokenSource()) { cts.Cancel(); await Reject<OperationCanceledException>(()=>SocketFrames.ReadBinary(new FakeSocket(payload,1),cts.Token), "receive cancellation respected"); }
if(args.Contains("--public-relay"))
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    using var host = new ClientWebSocket();
    const string endpoint="wss://megabonk-together-matchmaking.balatro-vs-matchmaking.eu/ws?friendlies";
    await host.ConnectAsync(new Uri(endpoint+"&role=Host&name=BonkLinkCheck&enabledSharedExperience=True"),cts.Token);
    var reply=MemoryPackSerializer.Deserialize<IWsMessage>(await SocketFrames.ReadBinary(host,cts.Token)) as MatchmakingServerConnectionStatus;
    Check(reply?.HasJoined==true && !string.IsNullOrEmpty(reply.RoomCode), "public relay created private room");
    var clients=new List<ClientWebSocket>();
    try
    {
        for(int i=0;i<4;i++)
        {
            var client=new ClientWebSocket(); clients.Add(client);
            await client.ConnectAsync(new Uri(endpoint+"&role=Client&code="+Uri.EscapeDataString(reply!.RoomCode)+"&name=BonkLinkCheck"+i+"&enabledSharedExperience=True"),cts.Token);
            var joined=MemoryPackSerializer.Deserialize<IWsMessage>(await SocketFrames.ReadBinary(client,cts.Token)) as MatchmakingServerConnectionStatus;
            Check(joined?.HasJoined==true, "private room accepted player "+(i+2));
        }
    }
    finally { foreach(var client in clients) client.Dispose(); }
}

// BonkLink edition, 2026-09-13: co-op world checkpoints.
{
    var root = Path.Combine(Path.GetTempPath(), "bonklink-worlds-" + Guid.NewGuid().ToString("N"));
    try
    {
        var hostId = Guid.NewGuid();
        var worldId = Guid.NewGuid();

        SavedPlayer MakePlayer(string name, Guid id, bool host, uint connection) => new()
        {
            PlayerId = id,
            Identity = id.ToString("N"),
            ConnectionId = connection,
            Name = name,
            Character = 3,
            IsHost = host,
            Connected = true,
            Pose = new SavedPose { X = 12.5f, Y = 3.25f, Z = -40.75f, Yaw = 91.5f },
            Hp = 87,
            MaxHp = 140,
            Overheal = 12.5f,
            Shield = 30.5f,
            MaxShield = 45f,
            Gold = 1234,
            Xp = 5600,
            Level = 17,
            LeftOverXp = 42.25f,
            Banishes = 2,
            Refreshes = 1,
            Skips = 3,
            Items = new Dictionary<int, int> { { 4, 3 }, { 11, 1 }, { 27, 9 } },
            // The upgrade picked at each level-up. Losing these is what made a restored
            // character keep its level and none of what that level gave it.
            Stats =
            {
                new SavedModifier { Stat = 2, Operation = 0, Value = 25f },
                new SavedModifier { Stat = 2, Operation = 1, Value = 0.15f },
                new SavedModifier { Stat = 9, Operation = 0, Value = 4f },
            },
            Upgrades =
            {
                new SavedUpgrade
                {
                    Kind = SavedUpgrade.WeaponKind, Type = 2, Level = 3, Enabled = true,
                    Levels =
                    {
                        new SavedModifierSet { Modifiers = { new SavedModifier { Stat = 1, Operation = 0, Value = 1.5f } } },
                        new SavedModifierSet { Modifiers = { new SavedModifier { Stat = 1, Operation = 1, Value = 0.25f } } },
                        new SavedModifierSet { Modifiers = { new SavedModifier { Stat = 7, Operation = 0, Value = 3f } } },
                    }
                },
                new SavedUpgrade
                {
                    Kind = SavedUpgrade.TomeKind, Type = 5, Level = 4, Rarity = 2,
                    Levels = { new SavedModifierSet { Modifiers = { new SavedModifier { Stat = 9, Operation = 1, Value = 0.8f } } } }
                },
            }
        };

        WorldSave MakeWorld(long revision)
        {
            var save = new WorldSave
            {
                WorldId = worldId,
                HostId = hostId,
                Revision = revision,
                SavedAt = DateTimeOffset.UtcNow,
                GameVersion = "5.1.0",
                ModVersion = "5.1.0",
                Name = "Forest - Stage 2",
                Map = 1,
                Stage = "Stage2",
                StageIndex = 1,
                Tier = 3,
                Challenge = "Cursed",
                Music = 2,
                Seed = 987654,
                ElapsedSeconds = 742.5,
                SharedRewards = true,
                Resumable = true,
                Progress = new WorldProgress
                {
                    BossCurses = 2, EnteredBossRoom = true, IsCrypt = false, CryptIndex = 1, DungeonTimeToComplete = 90f,
                    ConsumedObjects =
                    {
                        new SavedObject { Prefab = "Chest", Pose = new SavedPose { X = 100.5f, Y = 2f, Z = -18.25f } },
                        new SavedObject { Prefab = "Chest", Pose = new SavedPose { X = -60f, Y = 0f, Z = 4f } },
                        new SavedObject { Prefab = "Shrine", Pose = new SavedPose { X = 12f, Y = 1f, Z = 77f } },
                    }
                },
            };
            save.Players.Add(MakePlayer("Host", hostId, true, 0));
            save.Pickups.Add(new SavedPickup { Kind = 1, Value = 731, ReadyDelay = 1.25f, Pose = new SavedPose { X = 7, Y = 2, Z = -9 } });
            save.Players.Add(MakePlayer("Friend", Guid.NewGuid(), false, 7));
            save.Players.Add(MakePlayer("Stranger", Guid.NewGuid(), false, 9));
            for (uint i = 1; i <= 1500; i++)
                save.Enemies.Add(new SavedEnemy { NetworkId = i, Species = (int)(i % 30), Hp = 40 + i % 17, MaxHp = 80, Wave = (int)(i % 5), Pose = new SavedPose { X = i, Z = -i } });
            save.Enemies.Add(new SavedEnemy { NetworkId = 90001, Species = 42, IsBoss = true, Hp = 18450.5f, MaxHp = 42000f, Armor = 3, ArmorMax = 5, Flags = 2, Pose = new SavedPose { X = 5, Y = 1, Z = 5 } });
            save.Enemies.Add(new SavedEnemy { NetworkId = 90002, Species = 43, IsBoss = true, IsFinalBoss = true, Hp = 99000f, MaxHp = 120000f, Flags = 4 });
            for (uint i = 1; i <= 12; i++) save.BossOrbs.Add(new SavedBossOrb { NetworkId = i, Hp = 100 + i, Pose = new SavedPose { X = i } });
            for (uint i = 1; i <= 40; i++) save.Objects.Add(new SavedObject { NetworkId = i, Prefab = "Chest", Active = true, Pose = new SavedPose { X = i * 3, Z = i } });
            return save;
        }

        var store = new WorldSaveStore(root);
        var original = MakeWorld(1);
        original.Enemies[0].AttackCooldowns["charge"] = 4.75f;
        original.Scaling.BossHealthPerPlayer = 2.5f;
        original.Enemies[0].Debuffs.Add(new SavedDebuff { Kind = 1, Ticks = 13, Stacks = 4, Damage = 9.5f, Source = "poison" });
        store.Write(original);
        Check(File.Exists(store.PathFor(worldId)), "co-op checkpoint written to its own world file");
        Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "checkpoint write leaves no temporary files");

        // A brand new store, as if the game had been closed and started again.
        var reopened = new WorldSaveStore(root);
        var loaded = reopened.Read(worldId);
        Check(loaded.Scaling.BossHealthPerPlayer == 2.5f, "world retains host scaling choices");
        Check(loaded.Enemies[0].Debuffs.Single().Ticks == 13 && loaded.Enemies[0].Debuffs[0].Stacks == 4 && loaded.Enemies[0].Debuffs[0].Damage == 9.5f, "enemy effect ticks stacks and damage survive reopening");
        Check(loaded.Pickups.Single().Value == 731 && loaded.Pickups[0].ReadyDelay == 1.25f && loaded.Pickups[0].Pose.Z == -9, "ground pickup value, delay and position survive reopening");
        Check(loaded.Enemies[0].AttackCooldowns["charge"] == 4.75f, "enemy attack remaining cooldown survives reopening");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Pickups[0].Value = -1)), "rejects negative ground pickup values");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Pickups[0].ReadyDelay = float.NaN)), "rejects invalid pickup deadlines");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Enemies[0].AttackCooldowns["charge"] = float.PositiveInfinity)), "rejects invalid attack deadlines");
        Check(loaded.Revision == 1 && loaded.WorldId == worldId && loaded.HostId == hostId, "reloaded checkpoint keeps its identity");
        Check(loaded.Stage == "Stage2" && loaded.Map == 1 && loaded.Tier == 3 && loaded.Seed == 987654 && loaded.Challenge == "Cursed", "reloaded checkpoint keeps run configuration");
        Check(Math.Abs(loaded.ElapsedSeconds - 742.5) < 1e-9 && loaded.Progress.BossCurses == 2 && loaded.Progress.EnteredBossRoom, "reloaded checkpoint keeps world progress");
        Check(loaded.Players.Count == 3 && loaded.Players.Count(p => p.IsHost) == 1, "reloaded checkpoint keeps every player");

        var reloadedPlayer = loaded.Players.First(p => p.Name == "Friend");
        Check(reloadedPlayer.Hp == 87 && reloadedPlayer.MaxHp == 140 && Math.Abs(reloadedPlayer.Shield - 30.5f) < 1e-6 && Math.Abs(reloadedPlayer.Overheal - 12.5f) < 1e-6, "reloaded player keeps health, shield and overheal");
        Check(reloadedPlayer.Gold == 1234 && reloadedPlayer.Xp == 5600 && reloadedPlayer.Level == 17 && reloadedPlayer.Banishes == 2 && reloadedPlayer.Skips == 3, "reloaded player keeps gold, experience and rerolls");
        Check(reloadedPlayer.Items.Count == 3 && reloadedPlayer.Items[27] == 9, "reloaded player keeps every item and its count");
        var reloadedWeapon = reloadedPlayer.Weapons.Single();
        Check(reloadedWeapon.Type == 2 && reloadedWeapon.Level == 3 && reloadedWeapon.Levels.Count == 3, "reloaded player keeps weapon levels");
        Check(Math.Abs(reloadedWeapon.Levels[1].Modifiers.Single().Value - 0.25f) < 1e-6 && reloadedWeapon.Levels[2].Modifiers.Single().Stat == 7, "reloaded weapon keeps every upgrade modifier in order");
        Check(reloadedPlayer.Tomes.Single().Level == 4 && reloadedPlayer.Tomes.Single().Rarity == 2, "reloaded player keeps tome level and rarity");
        Check(Math.Abs(reloadedPlayer.Pose.X - 12.5f) < 1e-6 && Math.Abs(reloadedPlayer.Pose.Z + 40.75f) < 1e-6, "reloaded player keeps their position");

        Check(loaded.Enemies.Count == 1502, "reloaded checkpoint keeps every enemy");
        var reloadedBoss = loaded.Enemies.Single(e => e.NetworkId == 90001);
        Check(reloadedBoss.IsBoss && Math.Abs(reloadedBoss.Hp - 18450.5f) < 1e-3 && Math.Abs(reloadedBoss.MaxHp - 42000f) < 1e-3 && reloadedBoss.Armor == 3, "reloaded boss keeps its health and armour");
        Check(loaded.Enemies.Single(e => e.IsFinalBoss).Hp > 98999f, "reloaded final boss keeps its health");
        Check(loaded.BossOrbs.Count == 12 && Math.Abs(loaded.BossOrbs.Last().Hp - 112f) < 1e-6, "reloaded checkpoint keeps boss orbs");
        Check(reopened.Latest(hostId)?.Revision == 1, "the newest resumable checkpoint is offered for resume");
        Check(loaded.Objects.Count == 40 && loaded.Objects.All(o => o.Prefab == "Chest"), "reloaded checkpoint keeps the world objects still standing");
        Check(loaded.Progress.ConsumedObjects.Count == 3 && Math.Abs(loaded.Progress.ConsumedObjects[0].Pose.X - 100.5f) < 1e-6, "reloaded checkpoint keeps what the players already used");

        // The rebuilt stage is matched the way the host matches it: same thing, same place.
        bool WasConsumed(List<SavedObject> remaining, string prefab, float x, float y, float z)
        {
            for (var i = 0; i < remaining.Count; i++)
            {
                var c = remaining[i];
                if (c.Prefab != prefab) continue;
                var dx = c.Pose.X - x; var dy = c.Pose.Y - y; var dz = c.Pose.Z - z;
                if (dx * dx + dy * dy + dz * dz > 1.25f * 1.25f) continue;
                remaining.RemoveAt(i);
                return true;
            }
            return false;
        }

        var remaining = loaded.Progress.ConsumedObjects.ToList();
        Check(WasConsumed(remaining, "Chest", 100.5f, 2f, -18.25f), "a chest that was opened is dropped when the stage is rebuilt");
        Check(!WasConsumed(remaining, "Chest", 100.5f, 2f, -18.25f), "each used object accounts for exactly one rebuilt object");
        Check(!WasConsumed(remaining, "Shrine", -60f, 0f, 4f), "a different kind of object in the same place is kept");
        Check(!WasConsumed(remaining, "Chest", -60f, 0f, 9f), "an object too far from the record is kept");
        Check(WasConsumed(remaining, "Chest", -60.4f, 0.3f, 4.2f), "a small placement difference still matches");
        Check(remaining.Count == 1 && !WasConsumed(new List<SavedObject>(), "Chest", 100.5f, 2f, -18.25f), "a stage with nothing recorded comes back whole");

        // The state the host sends each peer travels as the same records.
        var wire = System.Text.Json.JsonSerializer.Serialize(loaded.Players);
        var overTheWire = System.Text.Json.JsonSerializer.Deserialize<List<SavedPlayer>>(wire)!;
        Check(overTheWire.Count == 3 && overTheWire.First(p => p.Name == "Friend").Weapons.Single().Levels.Count == 3, "player state survives the restore message");
        Check(loaded.FindPlayer(reloadedPlayer.Identity, 3)?.Name == "Friend", "a returning player is matched by installation identity and character");
        Check(loaded.FindPlayer("", 3) == null && loaded.FindPlayer(Guid.NewGuid().ToString("N"), 3) == null, "an unknown installation is not handed someone else's character");
        Check(loaded.FindPlayer(reloadedPlayer.Identity, 9) == null, "playing a character you have not used on this world starts fresh");
        Check(loaded.FindMostRecent(reloadedPlayer.Identity)?.Character == 3, "the world remembers the character you last played on it");
        Check(loaded.ConnectedCount == 3, "the world knows who was actually in the run");

        // A world keeps every character a player has used on it, not just the latest.
        var twoCharacters = MakeWorld(11);
        var veteran = twoCharacters.Players.First(p => p.Name == "Friend");
        var otherCharacter = MakePlayer("Friend", Guid.NewGuid(), false, 7);
        otherCharacter.Identity = veteran.Identity;
        otherCharacter.Character = 9;
        otherCharacter.Connected = false;
        otherCharacter.Gold = 55;
        twoCharacters.Players.Add(otherCharacter);
        WorldSaveStore.Validate(twoCharacters);
        Check(twoCharacters.FindPlayer(veteran.Identity, 3)?.Gold == 1234 && twoCharacters.FindPlayer(veteran.Identity, 9)?.Gold == 55, "one player keeps a separate run for each character on a world");
        Check(twoCharacters.FindMostRecent(veteran.Identity)?.Character == 3, "the connected character is the most recent one");

        // Players who are not in the run right now are still remembered by the world.
        var withAbsentees = MakeWorld(12);
        for (var i = 0; i < 8; i++)
        {
            var absent = MakePlayer("Absent" + i, Guid.NewGuid(), false, (uint)(30 + i));
            absent.Connected = false;
            withAbsentees.Players.Add(absent);
        }
        WorldSaveStore.Validate(withAbsentees);
        Check(withAbsentees.Players.Count == 11 && withAbsentees.ConnectedCount == 3, "a world remembers more players than a lobby holds");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(13), w => { foreach (var p in w.Players) p.Connected = true; for (var i = 0; i < 4; i++) { var extra = MakePlayer("E" + i, Guid.NewGuid(), false, (uint)(40 + i)); extra.Connected = true; w.Players.Add(extra); } })), "rejects more connected players than a lobby holds");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(14), w => { var clone = MakePlayer("Twin", Guid.NewGuid(), false, 21); clone.Identity = w.Players[1].Identity; clone.Character = w.Players[1].Character; w.Players.Add(clone); })), "rejects the same player twice on the same character");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(15), w => { var clone = MakePlayer("Twin", Guid.NewGuid(), false, 21); clone.Identity = w.Players[1].Identity; clone.Character = 9; clone.Connected = true; w.Players.Add(clone); })), "rejects one player connected as two characters at once");

        // Losing these is what made a restored character keep its level and none of its power.
        var storedStats = new WorldSaveStore(root).Read(worldId).Players.First(p => p.IsHost).Stats;
        Check(storedStats.Count == 3
              && storedStats[0].Stat == 2 && storedStats[0].Operation == 0 && storedStats[0].Value == 25f
              && storedStats[1].Operation == 1 && storedStats[1].Value == 0.15f
              && storedStats[2].Stat == 9 && storedStats[2].Value == 4f,
              "level-up stat upgrades survive a checkpoint exactly");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(16), w => w.Players[0].Stats[1].Value = float.NaN)), "rejects an unusable stat upgrade");
        // A world written before stat upgrades were saved must still open, or updating would
        // make every world a player already had vanish from their list.
        var older = MakeWorld(18); older.Schema = WorldSaveStore.OldestReadableSchema; older.Players[0].Stats.Clear();
        WorldSaveStore.Validate(older);
        Check(true, "a world saved by the previous version still loads");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(19), w => w.Schema = WorldSaveStore.OldestReadableSchema - 1)), "rejects a checkpoint older than the oldest readable version");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(20), w => w.Schema = WorldSaveStore.Schema + 1)), "rejects a checkpoint from a newer version");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(17), w => w.Players[0].Stats = null!)), "rejects a checkpoint with no stat upgrade list");

        // Coming back after closing the game. The slot is found by installation identity, and the
        // character a client reports on rejoin is not always the one they played -- reading that as
        // "never played here" handed them a blank character and lost everything they had.
        var rejoinWorld = MakeWorld(21);
        var returning = rejoinWorld.Players[1];
        returning.Character = 3;
        returning.Level = 37;
        Check(rejoinWorld.FindPlayer(returning.Identity, 3) != null, "a returning player is found on the character they played");
        Check(rejoinWorld.FindPlayer(returning.Identity, 99) == null, "a character they never played is not mistaken for theirs");
        Check(rejoinWorld.FindMostRecent(returning.Identity)?.Level == 37, "their most recent character is still found when the reported one does not match");
        Check(rejoinWorld.FindMostRecent("someone-who-never-played") == null, "a genuine newcomer is not handed somebody else's character");

        var second = MakeWorld(2);
        second.Players[0].Gold = 4321;
        reopened.Write(second);
        Check(new WorldSaveStore(root).Read(worldId).Players.First(p => p.IsHost).Gold == 4321, "a later checkpoint replaces the previous one");
        Check(new WorldSaveStore(root).Read(worldId, previous: true).Revision == 1, "the previous checkpoint is retained");

        Refuses(() => reopened.Write(MakeWorld(2)), "refuses to overwrite a checkpoint with the same revision");
        Refuses(() => reopened.Write(MakeWorld(1)), "refuses to overwrite a newer checkpoint");
        var otherHost = MakeWorld(3); otherHost.HostId = Guid.NewGuid(); otherHost.Players[0].PlayerId = otherHost.HostId;
        Refuses(() => reopened.Write(otherHost), "refuses a checkpoint written by another host");

        // A corrupted current file must not lose the run: the retained copy is used instead.
        var bytes = File.ReadAllBytes(store.PathFor(worldId));
        File.WriteAllBytes(store.PathFor(worldId), bytes.Take(bytes.Length / 2).ToArray());
        Check(new WorldSaveStore(root).TryRead(worldId, out var recovered, out var note) && recovered.Revision == 1 && note.Length > 0, "a truncated checkpoint falls back to the retained copy");
        // Flip one character inside the stored payload; the digest must catch it.
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var payloadAt = text.IndexOf("\"Payload\":\"", StringComparison.Ordinal) + 11;
        var flipAt = payloadAt + 40;
        var tampered = text[..flipAt] + (text[flipAt] == 'A' ? 'B' : 'A') + text[(flipAt + 1)..];
        File.WriteAllText(store.PathFor(worldId), tampered);
        Check(new WorldSaveStore(root).TryRead(worldId, out var afterTamper, out _) && afterTamper.Revision == 1, "an edited checkpoint fails its checksum and the retained copy is used");
        File.WriteAllBytes(store.PathFor(worldId), bytes);

        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players.Clear())), "rejects a checkpoint with no players");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => { for (var i = 0; i < 4; i++) w.Players.Add(MakePlayer("Extra" + i, Guid.NewGuid(), false, (uint)(20 + i))); })), "rejects more players than a lobby holds");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[1].PlayerId = w.Players[0].PlayerId)), "rejects duplicate player identity");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[1].Identity = w.Players[0].Identity)), "rejects duplicate installation identity");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[1].IsHost = true)), "rejects a checkpoint with two hosts");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Enemies[3].Hp = float.NaN)), "rejects unusable enemy health");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Enemies[3].Pose.Y = float.PositiveInfinity)), "rejects unusable enemy placement");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[0].Hp = w.Players[0].MaxHp + 50)), "rejects health above the maximum");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[0].Items[3] = -1)), "rejects a negative item count");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[0].Upgrades[0].Levels[0].Modifiers[0].Value = float.NaN)), "rejects an unusable stat modifier");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Players[0].Upgrades[0].Kind = "trinket")), "rejects an unknown upgrade kind");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Stage = "")), "refuses to advertise a resume with no stage");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.ElapsedSeconds = double.NaN)), "rejects an unusable run timer");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Progress.ConsumedObjects[0].Pose.X = float.NaN)), "rejects an unusable used-object record");
        Refuses(() => WorldSaveStore.Validate(Broken(MakeWorld(9), w => w.Schema = 1)), "rejects a checkpoint from an older schema");

        var unfinished = MakeWorld(4); unfinished.Resumable = false; unfinished.MissingState.Add("enemies");
        reopened.Write(unfinished);
        Check(reopened.Latest(hostId) == null, "a checkpoint with missing state is never offered for resume");

        for (var i = 0; i < 4; i++)
        {
            var extra = MakeWorld(1);
            extra.WorldId = Guid.NewGuid();
            extra.SavedAt = DateTimeOffset.UtcNow.AddMinutes(i + 1);
            reopened.Write(extra);
        }
        Check(reopened.ListWorlds().Count() == 5, "each run keeps its own world file");
        Check(reopened.Prune(2) == 3 && reopened.ListWorlds().Count() == 2, "old worlds are pruned and the newest kept");
        var survivor = reopened.ListReadable().First().WorldId;
        reopened.Delete(survivor);
        Check(!reopened.ListWorlds().Contains(survivor) && !File.Exists(reopened.PathFor(survivor) + ".previous"), "deleting a world removes it and its retained copy");
    }
    finally
    {
        try { Directory.Delete(root, true); } catch { }
    }
}

// --- Everything that crosses the wire must actually be able to cross it. ---------------------
// A message type compiles happily with a member whose type nothing can serialize; it only fails
// when it is sent, and then it fails on every frame. That is what happened: one such member on
// the player model made the host's lobby broadcast throw continuously, and because that call sits
// in the middle of the host's update everything after it stopped running too -- remote player
// positions, enemy spawning, projectiles, checkpoints and the level-up choice handling all went
// with it. Clients saw everybody standing still at spawn on an empty map. So this walks every
// serializable type and proves its members are serializable too, before anything ships.
{
    var assembly = typeof(MegabonkTogether.Common.Messages.LobbyUpdates).Assembly;
    bool IsPackable(Type t) => t.GetCustomAttributes(typeof(MemoryPackableAttribute), false).Length > 0;

    IEnumerable<Type> Carried(Type t)
    {
        if (t.IsByRef || t.IsPointer) yield break;
        if (t.IsArray) { var e = t.GetElementType(); if (e != null) foreach (var c in Carried(e)) yield return c; yield break; }
        if (t.IsGenericType) { foreach (var a in t.GetGenericArguments()) foreach (var c in Carried(a)) yield return c; yield break; }
        yield return t;
    }

    var unserializable = new List<string>();
    foreach (var owner in assembly.GetTypes().Where(IsPackable))
    {
        var members = owner.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(f => (f.Name, Type: f.FieldType))
            .Concat(owner.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Select(p => (p.Name, Type: p.PropertyType)));

        foreach (var (name, type) in members)
            foreach (var carried in Carried(type))
            {
                // Only types this mod defines can be got wrong here; the framework's own and
                // Unity's are handled by MemoryPack itself.
                if (carried.Assembly != assembly) continue;
                if (carried.IsEnum || carried.IsInterface) continue;
                if (IsPackable(carried)) continue;
                unserializable.Add($"{owner.Name}.{name} carries {carried.Name}, which is not [MemoryPackable]");
            }
    }
    Check(unserializable.Count == 0, unserializable.Count == 0
        ? "every type sent over the wire can be serialized"
        : "UNSERIALIZABLE: " + string.Join("; ", unserializable));
}

// The exact message that was failing, with the exact member that broke it, actually sent and read
// back. The walk above is the general rule; this is the case that cost a day.
{
    var lobby = new MegabonkTogether.Common.Messages.LobbyUpdates
    {
        Players = new List<MegabonkTogether.Common.Models.Player>
        {
            new()
            {
                ConnectionId = 7,
                Name = "player",
                Stats = new List<SavedModifier> { new() { Stat = 3, Operation = 1, Value = 1.5f } },
            },
        },
    };
    var bytes = MemoryPackSerializer.Serialize(lobby);
    var readBack = MemoryPackSerializer.Deserialize<MegabonkTogether.Common.Messages.LobbyUpdates>(bytes);
    var stats = readBack!.Players.Single().Stats;
    Check(stats.Count == 1 && stats[0].Stat == 3 && stats[0].Value == 1.5f,
        "a lobby broadcast carrying a player's stat upgrades survives a round trip");

    var reported = MemoryPackSerializer.Deserialize<MegabonkTogether.Common.Messages.GameNetworkMessages.PlayerStatsReported>(
        MemoryPackSerializer.Serialize(new MegabonkTogether.Common.Messages.GameNetworkMessages.PlayerStatsReported
        {
            ConnectionId = 7,
            Stats = new List<SavedModifier> { new() { Stat = 2, Operation = 0, Value = 4f } },
        }));
    Check(reported!.Stats.Single().Value == 4f, "a player reporting their stat upgrades survives a round trip");
}

Console.WriteLine($"{passed} checks passed");

sealed class FakeSocket(byte[] bytes,int fragment,WebSocketMessageType type=WebSocketMessageType.Binary):WebSocket
{
    int offset;
    public override WebSocketCloseStatus? CloseStatus=>null;
    public override string? CloseStatusDescription=>null;
    public override WebSocketState State=>WebSocketState.Open;
    public override string? SubProtocol=>null;
    public override void Abort() { }
    public override void Dispose() { }
    public override Task CloseAsync(WebSocketCloseStatus s,string? d,CancellationToken c)=>Task.CompletedTask;
    public override Task CloseOutputAsync(WebSocketCloseStatus s,string? d,CancellationToken c)=>Task.CompletedTask;
    public override Task SendAsync(ArraySegment<byte> b,WebSocketMessageType t,bool e,CancellationToken c)=>Task.CompletedTask;
    public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int count=Math.Min(Math.Min(fragment,buffer.Count),bytes.Length-offset);
        bytes.AsSpan(offset,count).CopyTo(buffer.AsSpan()); offset+=count;
        return Task.FromResult(new WebSocketReceiveResult(count,type,offset==bytes.Length));
    }
}
