using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleNusantara.Core;

// Adventure: quest graph, dialogue, the Mystic Cave and save/load.
public sealed partial class Match
{
    // Ten seconds a line: long enough for the recorded voice of every line.
    public const int DialogueLineTicks = 100, MaxSaveLength = 200_000;
    private static readonly MapPoint KeeperPosition = new(30, 20), HeirloomPosition = new(88, 12), CaptainPosition = new(10, 60);
    private bool sideStarted, sideDone;
    /// <summary>The side quest generated from <see cref="Seed"/>.</summary>
    public SideQuestPlan SidePlan => Story.PlanSideQuest(Seed);
    private readonly HashSet<string> questsDone = new();
    private readonly Queue<string> pendingDialogues = new();
    private string? dialogueKey;
    private int dialogueLine, dialogueTicks;

    private void SetupAdventure()
    {
        Spawn(-1, "relic", 2, 27, 29, 1, 0, "#e6c986");
        Spawn(-1, "relic", 2, 60, 38, 1, 0, "#e6c986");
        Spawn(-1, "relic", 2, 75, 72, 1, 0, "#e6c986");
        // Sealed until the inscriptions and the heirloom keris are both in hand.
        var kalatidha = Spawn(-1, "boss", 1, 84, 83, 700, 16, "#e57c86"); kalatidha.Invulnerable = true; kalatidha.Model = "kalatidha";
        for (var i = 0; i < 5; i++) Spawn(-1, "guardian", 1, 35 + i * 9, 30 + i * 10, 100, 7, "#c8789a");
        Spawn(-1, "npc", 2, KeeperPosition.X, KeeperPosition.Z, 1, 0, "#83c9ba").Model = Story.KeeperNpc;
        Spawn(-1, "npc", 2, CaptainPosition.X, CaptainPosition.Z, 1, 0, "#e0b07a").Model = Story.CaptainNpc;
        Spawn(-1, "artifact", 2, HeirloomPosition.X, HeirloomPosition.Z, 1, 0, "#f2d27a");
        Spawn(-1, "warden", 1, 78, 14, 120, 9, "#b06a9c");
        Spawn(-1, "warden", 1, 88, 22, 120, 9, "#b06a9c");
        Spawn(-1, "warden", 1, 80, 22, 120, 9, "#b06a9c");
        Say("intro");
    }

    private void Say(string key)
    {
        if (dialogueKey is null) { dialogueKey = key; dialogueLine = 0; dialogueTicks = 0; }
        else pendingDialogues.Enqueue(key);
    }

    private void NextLine()
    {
        dialogueTicks = 0;
        if (dialogueKey is null) return;
        if (++dialogueLine < Story.Dialogues[dialogueKey].Length) return;
        dialogueLine = 0;
        dialogueKey = pendingDialogues.Count > 0 ? pendingDialogues.Dequeue() : null;
    }

    private Unit? HeroNear(MapPoint point, double radius) =>
        units.Find(u => u.Kind == "hero" && u.Health > 0 && Distance(u.X, u.Z, point.X, point.Z) <= radius);

    private void AdvanceStory()
    {
        // Lines also advance on their own, so dialogue never blocks a shared room.
        if (dialogueKey is not null && ++dialogueTicks >= DialogueLineTicks) NextLine();
        foreach (var quest in Story.Quests)
        {
            if (questsDone.Contains(quest.Id) || !quest.Requires.All(questsDone.Contains)) continue;
            var met = quest.Id switch
            {
                Story.Keeper => HeroNear(KeeperPosition, 6) is not null,
                Story.Inscriptions => Relics == 3,
                Story.Guardians => !units.Exists(u => u.Kind == "guardian"),
                Story.Heirloom => HeroNear(HeirloomPosition, 5) is not null,
                _ => !units.Exists(u => u.Kind == "boss")
            };
            if (!met) continue;
            questsDone.Add(quest.Id);
            Gold += 60;
            switch (quest.Id)
            {
                case Story.Guardians:
                    navigation = NavigationMap.AdventureOpen;
                    foreach (var unit in units) { unit.Route = []; unit.RouteIndex = 0; }
                    Say("gua-terbuka");
                    break;
                case Story.Heirloom:
                    units.RemoveAll(u => u.Kind == "artifact");
                    foreach (var hero in units) if (hero.Kind == "hero") hero.Damage += 15;
                    Say(Story.Heirloom);
                    break;
                case Story.Kalatidha: Say("akhir"); break;
                default: Say(quest.Id); break;
            }
            Message = $"Tugas selesai: {quest.Title}. / Quest complete: {quest.TitleEn}.";
        }
        AdvanceSideQuest();
        var boss = units.Find(u => u.Kind == "boss");
        if (boss is { Invulnerable: true } && Story.Quest(Story.Kalatidha).Requires.All(questsDone.Contains))
        {
            boss.Invulnerable = false;
            Say(Story.Kalatidha);
        }
    }

    private void AdvanceSideQuest()
    {
        if (sideDone) return;
        var plan = SidePlan;
        if (!sideStarted)
        {
            if (HeroNear(CaptainPosition, 6) is null) return;
            sideStarted = true;
            foreach (var (x, z) in plan.Points)
            {
                if (units.Count >= MaxUnits) break;
                if (plan.Id == Story.Fragments) Spawn(-1, "flotsam", 2, x, z, 1, 0, "#b58a5a");
                else Spawn(-1, "spirit", 1, x, z, 90, 7, "#7fd6d0");
            }
            Say(Story.Captain); Say(plan.Id);
            Message = "Tugas sampingan dimulai. / Side quest started.";
            return;
        }
        if (units.Exists(u => u.Kind is "flotsam" or "spirit")) return;
        sideDone = true; Gold += 100;
        foreach (var hero in units) if (hero.Kind == "hero") { hero.MaxHealth += 40; hero.Health = Math.Min(hero.MaxHealth, hero.Health + 40); }
        Say("nakhoda-selesai");
        Message = $"Tugas sampingan selesai: {Story.Quest(plan.Id).Title}. / Side quest complete: {Story.Quest(plan.Id).TitleEn}.";
    }

    private QuestView SideQuestView()
    {
        if (!sideStarted) return new QuestView(Story.Captain, QuestState.Active, 0, 1);
        var plan = SidePlan;
        var left = units.Count(u => u.Kind is "flotsam" or "spirit");
        return new QuestView(plan.Id, sideDone ? QuestState.Done : QuestState.Active, plan.Count - left, plan.Count);
    }

    private QuestView[] QuestViews()
    {
        var views = new QuestView[Story.Quests.Count + 1];
        views[^1] = SideQuestView();
        for (var i = 0; i < Story.Quests.Count; i++)
        {
            var quest = Story.Quests[i];
            var done = questsDone.Contains(quest.Id);
            var (progress, goal) = quest.Id switch
            {
                Story.Inscriptions => (Relics, 3),
                Story.Guardians => (5 - units.Count(u => u.Kind == "guardian"), 5),
                _ => (done ? 1 : 0, 1)
            };
            views[i] = new QuestView(quest.Id, done ? QuestState.Done : quest.Requires.All(questsDone.Contains) ? QuestState.Active : QuestState.Locked, progress, goal);
        }
        return views;
    }

    private static readonly JsonSerializerOptions SaveOptions = new() { IncludeFields = false };
    private static readonly HashSet<string> SavableKinds = ["hero", "relic", "boss", "guardian", "npc", "artifact", "warden", "flotsam", "spirit", "creep", "wild", "loot"];
    private static readonly HashSet<string> SavableModels = new HashSet<string>(["", Story.KeeperNpc, Story.CaptainNpc, "kalatidha"])
        .Concat(Catalog.Creeps.Select(c => c.Model)).Concat(Catalog.Wildlife.Select(w => w.Model)).Concat(Catalog.Loot.Select(l => l.Id)).ToHashSet();

    /// <summary>
    /// Serializes a solo Adventure match in progress; null for anything else. Cached routes are dropped so that
    /// the saved match and this one continue identically.
    /// </summary>
    public string? Save()
    {
        if (Mode != GameMode.Adventure || Status != MatchStatus.Playing || players.Count != 1 || !players.ContainsKey(0)) return null;
        foreach (var unit in units) { unit.Route = []; unit.RouteIndex = 0; }
        var data = new SaveData
        {
            Version = SaveData.Current, Seed = Seed, SideStarted = sideStarted, SideDone = sideDone, Hero = players[0].Id, Tick = Tick, Gold = Gold, NextId = nextId, Message = Message,
            Cooldown = cooldowns[0], GuardCooldown = guardCooldowns[0], Collected = collected.Order().ToArray(),
            QuestsDone = Story.Quests.Select(q => q.Id).Where(questsDone.Contains).ToArray(),
            Camps = camps.Select(c => new SavedCamp { Id = c.Definition.Id, RespawnTicks = c.RespawnTicks, Clears = c.Clears, AlertTicks = c.AlertTicks }).ToArray(),
            Wild = wildRespawns.Select(w => new SavedWild { Model = w.Model, X = w.X, Z = w.Z, Ticks = w.Ticks }).ToArray(),
            Treasures = Treasures(0).ToArray(),
            DialogueKey = dialogueKey, DialogueLine = dialogueLine, DialogueTicks = dialogueTicks, Pending = pendingDialogues.ToArray(),
            Units = units.Select(u => new SavedUnit
            {
                Id = u.Id, Owner = u.Owner, Kind = u.Kind, Team = u.Team, Damage = u.Damage, Color = u.Color, X = u.X, Z = u.Z, TargetX = u.TargetX, TargetZ = u.TargetZ,
                Health = u.Health, MaxHealth = u.MaxHealth, AttackCooldown = u.AttackCooldown, OrderX = u.OrderX, OrderZ = u.OrderZ, Order = u.Order,
                GuardTicks = u.GuardTicks, Invulnerable = u.Invulnerable, RouteX = u.RouteX, RouteZ = u.RouteZ, Model = u.Kind == "hero" || u.Model.Length == 0 ? null : u.Model,
                CampId = u.CampId, HomeX = u.HomeX, HomeZ = u.HomeZ, SpeedBonus = u.SpeedBonus, Cargo = u.Cargo, ProvokedTicks = u.ProvokedTicks, Retreating = u.Retreating, Ticks = u.GatherTicks,
                Hit = u.LastHitTeam + 1, Boost = u.BoostTicks
            }).ToArray()
        };
        return JsonSerializer.Serialize(data, SaveOptions);
    }

    /// <summary>Rebuilds a match from <see cref="Save"/> output. Returns null when the data is malformed or implausible.</summary>
    public static Match? Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxSaveLength) return null;
        SaveData? data;
        try { data = JsonSerializer.Deserialize<SaveData>(json, SaveOptions); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException) { return null; }
        // Version 1 saves (v0.10 to v0.12) predate the captain and the side quest; they load with the side quest not started.
        // Versions 1 and 2 predate neutral camps and wildlife; those are placed fresh.
        if (data is not { Version: 1 or 2 or SaveData.Current, Units: not null, QuestsDone: not null, Collected: not null, Pending: not null, Hero: not null, Message: not null }) return null;
        if (!Catalog.Heroes.Any(h => h.Id == data.Hero) || data.Tick < 0 || data.Gold is < 0 or > 1_000_000 || data.Units.Length is 0 or > MaxUnits) return null;
        if (!Good(data.Cooldown, 0, 60) || !Good(data.GuardCooldown, 0, 60) || data.Message.Length > 300) return null;
        if (data.QuestsDone.Any(q => Story.Quests.All(known => known.Id != q)) || data.QuestsDone.Distinct().Count() != data.QuestsDone.Length) return null;
        // A finished quest implies its prerequisites: reject graphs that could not have been reached by play.
        if (data.QuestsDone.Any(q => !Story.Quest(q).Requires.All(data.QuestsDone.Contains))) return null;
        var keys = data.Pending.Append(data.DialogueKey).Where(k => k is not null).ToArray();
        if (keys.Length > 16 || keys.Any(k => !Story.Dialogues.ContainsKey(k!))) return null;
        if (data.DialogueKey is not null && (data.DialogueLine < 0 || data.DialogueLine >= Story.Dialogues[data.DialogueKey].Length)) return null;
        if (data.DialogueTicks is < 0 or > DialogueLineTicks) return null;
        if (data.Units.Count(u => u is { Kind: "hero", Owner: 0, Team: 0 }) != 1 || data.Units.Count(u => u?.Kind == "hero") != 1) return null;
        if (data.Units.Select(u => u?.Id ?? 0).Distinct().Count() != data.Units.Length) return null;
        foreach (var unit in data.Units)
        {
            if (unit?.Kind is null || unit.Color is null || !SavableKinds.Contains(unit.Kind) || unit.Color.Length > 9 || unit.Id <= 0 || unit.Id > data.NextId) return null;
            if (unit.Model is not null && !SavableModels.Contains(unit.Model)) return null;
            if (!Good(unit.HomeX, 0, 100) || !Good(unit.HomeZ, 0, 100) || !Good(unit.SpeedBonus, 0, 1) || unit.Cargo is < 0 or > 1000 || unit.ProvokedTicks is < 0 or > 1000 || unit.Ticks is < 0 or > 1000) return null;
            if (unit.Hit is < 0 or > 4 || unit.Boost is < 0 or > 100) return null;
            var neutral = unit.Kind is "creep" or "wild" or "loot";
            if (neutral && data.Version < SaveData.Current) return null;
            if (unit.Kind == "creep" && (Catalog.Creep(unit.Model ?? "") is null || Maps.Default(GameMode.Adventure).Camps.All(c => c.Id != unit.CampId))) return null;
            if (unit.Kind == "wild" && Catalog.Wild(unit.Model ?? "") is null) return null;
            if (unit.Kind == "loot" && Catalog.LootItem(unit.Model ?? "") is null) return null;
            if (unit.Team is < 0 or > 3 || (unit.Team == 3) != (unit.Kind is "creep" or "wild") || !Enum.IsDefined(unit.Order) || unit.Damage is < 0 or > 1000 || unit.GuardTicks is < 0 or > GuardTicks) return null;
            if (!Good(unit.X, 0, 100) || !Good(unit.Z, 0, 100) || !Good(unit.TargetX, 0, 100) || !Good(unit.TargetZ, 0, 100) || !Good(unit.OrderX, 0, 100) || !Good(unit.OrderZ, 0, 100)) return null;
            if (!Good(unit.RouteX, 0, 100) || !Good(unit.RouteZ, 0, 100) || !Good(unit.MaxHealth, 1, 5000) || !Good(unit.Health, 0.000001, unit.MaxHealth) || !Good(unit.AttackCooldown, -1e7, 10)) return null;
        }
        if (data.Collected.Length > 3 || data.Collected.Distinct().Count() != data.Collected.Length) return null;
        // Side-quest objects exist only while the side quest is running, and never more than it generated.
        var objectives = data.Units.Count(u => u.Kind is "flotsam" or "spirit");
        if (data.SideDone && !data.SideStarted) return null;
        if ((!data.SideStarted || data.SideDone) && objectives > 0) return null;
        if (data.SideStarted && objectives > Story.PlanSideQuest(data.Seed).Count) return null;
        if (data.Units.Count(u => u is { Kind: "npc", Model: Story.CaptainNpc }) > 1) return null;
        var campIds = Maps.Default(GameMode.Adventure).Camps.Select(c => c.Id).ToHashSet();
        if (data.Version == SaveData.Current)
        {
            if (data.Camps is null || data.Wild is null || data.Treasures is null) return null;
            if (data.Camps.Length != campIds.Count || data.Camps.Any(c => c is null || !campIds.Contains(c.Id) || c.RespawnTicks is < 0 or > 5000 || c.Clears is < 0 or > 10000 || c.AlertTicks is < 0 or > 1000)) return null;
            if (data.Camps.Select(c => c.Id).Distinct().Count() != data.Camps.Length) return null;
            if (data.Wild.Length > 64 || data.Wild.Any(w => w is null || Catalog.Wild(w.Model ?? "") is null || !Good(w.X, 0, 100) || !Good(w.Z, 0, 100) || w.Ticks is < 0 or > WildRespawnTicks)) return null;
            if (data.Treasures.Length > MaxTreasures || data.Treasures.Any(t => Catalog.LootItem(t ?? "") is null)) return null;
            if (data.Units.Count(u => u.Kind == "creep") > campIds.Count * 3 || data.Units.Count(u => u.Kind == "loot") > MaxLootOnGround) return null;
        }

        var match = new Match(GameMode.Adventure, data.Hero, data.Version == 1 ? 0 : data.Seed);
        match.units.Clear(); match.questsDone.Clear(); match.pendingDialogues.Clear(); match.collected.Clear();
        foreach (var u in data.Units)
            match.units.Add(new Unit
            {
                Id = u.Id, Owner = u.Owner, Kind = u.Kind!, Team = u.Team, Damage = u.Damage, Color = u.Color!, X = u.X, Z = u.Z, TargetX = u.TargetX, TargetZ = u.TargetZ,
                Health = u.Health, MaxHealth = u.MaxHealth, AttackCooldown = u.AttackCooldown, OrderX = u.OrderX, OrderZ = u.OrderZ, Order = u.Order,
                GuardTicks = u.GuardTicks, Invulnerable = u.Invulnerable, RouteX = u.RouteX, RouteZ = u.RouteZ, HomeX = 24, HomeZ = 24,
                Model = u.Kind == "hero" ? data.Hero : u.Kind == "npc" && string.IsNullOrEmpty(u.Model) ? Story.KeeperNpc : u.Kind == "boss" && string.IsNullOrEmpty(u.Model) ? "kalatidha" : u.Model ?? "",
                CampId = u.CampId, SpeedBonus = u.SpeedBonus, Cargo = u.Cargo, ProvokedTicks = u.ProvokedTicks, Retreating = u.Retreating, GatherTicks = u.Ticks,
                LastHitTeam = data.Version == SaveData.Current ? u.Hit - 1 : -1, BoostTicks = u.Boost
            });
        if (data.Version == SaveData.Current)
            foreach (var (saved, unit) in data.Units.Zip(match.units))
            {
                // Version 3 keeps every home; earlier versions only needed the hero's, which is its arrival point.
                unit.HomeX = saved.HomeX; unit.HomeZ = saved.HomeZ;
            }
        match.nextId = data.NextId;
        if (data.Version == 1 && !match.units.Exists(u => u.Model == Story.CaptainNpc) && match.units.Count < MaxUnits)
            match.Spawn(-1, "npc", 2, CaptainPosition.X, CaptainPosition.Z, 1, 0, "#e0b07a").Model = Story.CaptainNpc;
        match.sideStarted = data.SideStarted; match.sideDone = data.SideDone;
        if (data.Version == SaveData.Current)
        {
            foreach (var saved in data.Camps!)
            {
                var camp = match.camps.First(c => c.Definition.Id == saved.Id);
                camp.RespawnTicks = saved.RespawnTicks; camp.Clears = saved.Clears; camp.AlertTicks = saved.AlertTicks;
            }
            foreach (var wild in data.Wild!) match.wildRespawns.Add(new PendingWild(wild.Model!, wild.X, wild.Z, wild.Ticks));
            if (data.Treasures!.Length > 0) match.treasures[0] = data.Treasures.Select(t => t!).ToList();
        }
        else
        {
            // An older save: the camps and the wildlife arrive now.
            foreach (var camp in match.camps) match.SpawnCamp(camp);
            match.SpawnWildlife();
        }
        match.Tick = data.Tick; match.Gold = data.Gold; match.Message = data.Message;
        match.cooldowns[0] = data.Cooldown; match.guardCooldowns[0] = data.GuardCooldown;
        foreach (var id in data.Collected) match.collected.Add(id);
        foreach (var quest in data.QuestsDone) match.questsDone.Add(quest);
        match.dialogueKey = data.DialogueKey; match.dialogueLine = data.DialogueLine; match.dialogueTicks = data.DialogueTicks;
        foreach (var key in data.Pending) match.pendingDialogues.Enqueue(key);
        match.navigation = match.questsDone.Contains(Story.Guardians) ? NavigationMap.AdventureOpen : NavigationMap.AdventureClosed;
        return match;
    }

    private static bool Good(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;

    private sealed class SaveData
    {
        public const int Current = 3;
        public int Version { get; set; }
        public SavedCamp[]? Camps { get; set; }
        public SavedWild[]? Wild { get; set; }
        public string[]? Treasures { get; set; }
        public int Seed { get; set; }
        public bool SideStarted { get; set; }
        public bool SideDone { get; set; }
        public string? Hero { get; set; }
        public long Tick { get; set; }
        public int Gold { get; set; }
        public int NextId { get; set; }
        public string? Message { get; set; }
        public double Cooldown { get; set; }
        public double GuardCooldown { get; set; }
        public int[]? Collected { get; set; }
        public string[]? QuestsDone { get; set; }
        public string? DialogueKey { get; set; }
        public int DialogueLine { get; set; }
        public int DialogueTicks { get; set; }
        public string[]? Pending { get; set; }
        public SavedUnit[]? Units { get; set; }
    }

    private sealed class SavedUnit
    {
        public int Id { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Owner { get; set; }
        public string? Kind { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Team { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Damage { get; set; }
        public string? Color { get; set; }
        public double X { get; set; }
        public double Z { get; set; }
        public double TargetX { get; set; }
        public double TargetZ { get; set; }
        public double Health { get; set; }
        public double MaxHealth { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double AttackCooldown { get; set; }
        public double OrderX { get; set; }
        public double OrderZ { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public UnitOrder Order { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int GuardTicks { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Invulnerable { get; set; }
        public double RouteX { get; set; }
        public double RouteZ { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? Model { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int CampId { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double HomeX { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double HomeZ { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double SpeedBonus { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Cargo { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int ProvokedTicks { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Retreating { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Ticks { get; set; }
        /// <summary>LastHitTeam + 1, so the common "never hit" is the default and is not written.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Hit { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Boost { get; set; }
    }

    private sealed class SavedCamp
    {
        public int Id { get; set; }
        public int RespawnTicks { get; set; }
        public int Clears { get; set; }
        public int AlertTicks { get; set; }
    }

    private sealed class SavedWild
    {
        public string? Model { get; set; }
        public double X { get; set; }
        public double Z { get; set; }
        public int Ticks { get; set; }
    }
}
