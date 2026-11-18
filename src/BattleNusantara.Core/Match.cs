namespace BattleNusantara.Core;

public sealed record GameCommand(CommandKind Kind, double X = 0, double Z = 0, int[]? UnitIds = null, int? TargetId = null);
public sealed record UnitView(int Id, int Owner, string Kind, int Team, double X, double Z, double Health, double MaxHealth, string Color, UnitOrder Order, double TargetX, double TargetZ, int Cargo, int Construction = 100, int CancelRefund = 0, int Queue = 0, string Model = "");
public sealed record ResourceView(int Id, double X, double Z, int Remaining);
public sealed record VisionView(double X, double Z, double Radius);
public sealed record MatchView(GameMode Mode, MatchStatus Status, long Tick, int Gold, int Supplies, int Level, int Relics, double BaseHealth, double EnemyBaseHealth, double UltimateCooldown, string Message, UnitView[] Units, ObstacleView[] Obstacles, ResourceView[] Resources, int GoldCollected, VisionView[] Vision, double Respawn = 0, int Items = 0, double Buff = 0, double Experience = 0, double GuardCooldown = 0, double EnemyBuff = 0, QuestView[]? Quests = null, string Dialogue = "");

/// <summary>Deterministic 10 Hz simulation. Callers serialize access to each match.</summary>
public sealed partial class Match
{
    public const double StepSeconds = .1;
    public const int MaxUnits = 100;
    /// <summary>Human players in one co-op match. MOBA is limited to the five allied hero slots.</summary>
    public const int MaxPlayers = 10;
    public static int Capacity(GameMode mode) => mode == GameMode.Moba ? 5 : MaxPlayers;
    public const int MaxSelection = 24;
    public const int ConstructionTicks = 80;
    private NavigationMap navigation;
    private readonly List<Unit> units = new(MaxUnits);
    private readonly Dictionary<int, HeroDefinition> players = new();
    private readonly Dictionary<int, double> cooldowns = new();
    private int nextId;
    private long nextPriority;
    public const int RespawnTicks = 100, BossRespawnTicks = 900, BuffTicks = 600, TowersPerTeam = 3;
    private readonly List<Pending> respawns = new();
    private readonly Dictionary<int, int> items = new();
    public const int MaxHeroLevel = 6, GuardTicks = 40, ChampionGrowthTicks = 600, MaxChampionGrowth = 8;
    private readonly int[] buffTicks = new int[2];
    private int bossTicks, championGrowth;
    private readonly Dictionary<int, int> heroLevels = new();
    private readonly Dictionary<int, int> experience = new();
    private readonly Dictionary<int, double> guardCooldowns = new();
    // Ground the allied team has seen at least once, in 2-unit cells. Outposts may only be placed there.
    private readonly bool[] explored = new bool[50 * 50];
    private readonly List<ResourceNode> resources = new();
    public int GoldCollected { get; private set; }
    public const int MaxWorkersPerPlayer = 6;
    private readonly HashSet<int> collected = new();
    public GameMode Mode { get; }
    public MatchStatus Status { get; private set; }
    public long Tick { get; private set; }
    public int Gold { get; private set; } = 180;
    public int Supplies { get; private set; } = 12;
    public int Level { get; private set; } = 1;
    public int Relics => collected.Count;
    public double BaseHealth { get; private set; } = 900;
    public double EnemyBaseHealth { get; private set; } = 900;
    public string Message { get; private set; } = "Lindungi prasasti. / Protect the inscription.";

    /// <summary>Seeds what Adventure generates (the side quest). Equal seeds give equal matches.</summary>
    public int Seed { get; }

    public Match(GameMode mode, string heroId, int seed = 0)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Mode = mode; Seed = seed;
        navigation = mode switch { GameMode.Rts => NavigationMap.Rts, GameMode.Adventure => NavigationMap.AdventureClosed, _ => NavigationMap.Open };
        if (mode == GameMode.Rts)
        {
            resources.Add(new ResourceNode(1, 20, 34, 120));
            resources.Add(new ResourceNode(2, 47, 22, 300));
            resources.Add(new ResourceNode(3, 76, 48, 400));
        }
        AddPlayer(0, heroId);
        if (mode == GameMode.Adventure) SetupAdventure();
        else
        {
            if (mode == GameMode.Moba) SetupMoba();
            SpawnWave(1);
        }
        MarkExplored();
    }

    private void MarkExplored()
    {
        if (Mode != GameMode.Rts) return;
        foreach (var source in Vision())
        {
            var minX = Math.Max(0, (int)((source.X - source.Radius) / 2)); var maxX = Math.Min(49, (int)((source.X + source.Radius) / 2));
            var minZ = Math.Max(0, (int)((source.Z - source.Radius) / 2)); var maxZ = Math.Min(49, (int)((source.Z + source.Radius) / 2));
            for (var cz = minZ; cz <= maxZ; cz++)
                for (var cx = minX; cx <= maxX; cx++)
                {
                    var dx = cx * 2 + 1 - source.X; var dz = cz * 2 + 1 - source.Z;
                    if (dx * dx + dz * dz <= source.Radius * source.Radius) explored[cz * 50 + cx] = true;
                }
        }
    }

    /// <summary>True once the allied team has seen this ground. Always true outside RTS.</summary>
    public bool IsExplored(double x, double z) => Mode != GameMode.Rts
        || (double.IsFinite(x) && double.IsFinite(z) && x >= 0 && x < 100 && z >= 0 && z < 100 && explored[(int)(z / 2) * 50 + (int)(x / 2)]);

    private void SetupMoba()
    {
        // One tower per lane and side; a shrine or gate is exposed only after one of its towers falls.
        (double X, double Z)[] towers = [(20, 48), (36, 36), (48, 20)];
        foreach (var (x, z) in towers)
        {
            Spawn(-1, "tower", 0, x, z, 600, 22, "#83c9ba");
            Spawn(-1, "tower", 1, 100 - x, 100 - z, 600, 22, "#d17791");
        }
        int[] allyLanes = [0, 1, 2, 1]; int[] enemyLanes = [0, 1, 1, 2, 2];
        for (var i = 0; i < allyLanes.Length; i++) SpawnChampion(Catalog.AllyChampions[i], 0, allyLanes[i], i);
        for (var i = 0; i < enemyLanes.Length; i++) SpawnChampion(Catalog.EnemyChampions[i], 1, enemyLanes[i], i).Hunter = enemyLanes[i] == 2;
        SpawnBoss();
        SpawnWave(0);
    }

    private Unit SpawnChampion(HeroDefinition definition, int team, int lane, int slot)
    {
        var x = team == 0 ? 16 + slot * 3 : 84 - slot * 3; var z = team == 0 ? 20 : 80;
        var unit = Spawn(-1, "champion", team, x, z, definition.Health, definition.Damage, definition.Color);
        unit.HomeX = x; unit.HomeZ = z; unit.Model = definition.Id; SetLane(unit, lane);
        return unit;
    }

    private void SpawnBoss() => Spawn(-1, "boss", 3, 66, 34, 900, 30, "#e9c368").Model = "naga";

    private static void SetLane(Unit unit, int lane)
    {
        unit.LaneX = lane == 0 ? 20 : lane == 2 ? 80 : 50;
        unit.LaneZ = lane == 0 ? 80 : lane == 2 ? 20 : 50;
    }

    private int Towers(int team) => units.Count(u => u.Kind == "tower" && u.Team == team && u.Health > 0);

    /// <summary>True when the base of <paramref name="team"/> can take damage.</summary>
    public bool BaseExposed(int team) => Mode != GameMode.Moba || Towers(team) < TowersPerTeam;

    public const int MaxItemLevel = 3;
    /// <summary>Item levels are packed two bits per item in <see cref="MatchView.Items"/>.</summary>
    public static int ItemLevel(int packed, int item) => packed >> item * 2 & 3;
    /// <summary>Each further level costs one more multiple of the base price.</summary>
    public static int ItemCost(int item, int level) => Catalog.Items[item].Cost * (level + 1);

    private bool BuyItem(int owner, int? id)
    {
        if (Mode != GameMode.Moba || id is not { } index || index < 0 || index >= Catalog.Items.Count) return false;
        var item = Catalog.Items[index]; var owned = items.GetValueOrDefault(owner);
        var level = ItemLevel(owned, index); var cost = ItemCost(index, level);
        if (level >= MaxItemLevel || Gold < cost) return false;
        // The hero may be waiting to respawn; items apply either way and persist.
        var hero = units.Find(u => u.Owner == owner && u.Kind == "hero") ?? respawns.Find(r => r.Unit.Owner == owner && r.Unit.Kind == "hero")?.Unit;
        if (hero is null) return false;
        Gold -= cost; items[owner] = owned + (1 << index * 2);
        hero.Damage += item.Damage; hero.MaxHealth += item.Health; hero.SpeedBonus += item.Speed;
        if (hero.Health > 0) hero.Health += item.Health;
        Message = $"{item.Name} dibeli. / {item.NameEn} purchased.";
        return true;
    }

    public void AddPlayer(int owner, string heroId)
    {
        if (owner < 0 || owner >= MaxPlayers || players.ContainsKey(owner) || players.Count >= Capacity(Mode) || units.Count >= MaxUnits) throw new InvalidOperationException("Room is full.");
        var hero = Catalog.Hero(heroId);
        players.Add(owner, hero);
        cooldowns[owner] = 0; guardCooldowns[owner] = 0; heroLevels[owner] = 1; experience[owner] = 0;
        // The second player keeps the original arrival point; later players line up in two rows south of it.
        var (spawnX, spawnZ) = owner <= 1 ? (24d + owner * 5, 24d) : (17d + (owner - 2) % 4 * 4, 28d + (owner - 2) / 4 * 4);
        var unit = Spawn(owner, "hero", 0, spawnX, spawnZ, hero.Health, hero.Damage, hero.Color);
        unit.HomeX = unit.X; unit.HomeZ = unit.Z; unit.Model = hero.Id;
        if (Mode == GameMode.Moba && owner > 0)
        {
            // A joining player takes the place of one AI ally, keeping the side at five heroes.
            var replaced = units.FindLast(u => u.Kind == "champion" && u.Team == 0);
            var waiting = respawns.FindLastIndex(r => r.Unit.Kind == "champion" && r.Unit.Team == 0);
            if (replaced is not null) units.Remove(replaced);
            else if (waiting >= 0) respawns.RemoveAt(waiting);
        }
    }

    /// <summary>Removes a departed player. Their hero leaves; their other units pass to the remaining player.</summary>
    public void RemovePlayer(int owner)
    {
        if (!players.Remove(owner)) return;
        cooldowns.Remove(owner); guardCooldowns.Remove(owner); heroLevels.Remove(owner); experience.Remove(owner); items.Remove(owner);
        units.RemoveAll(u => u.Owner == owner && u.Kind == "hero");
        respawns.RemoveAll(r => r.Unit.Owner == owner && r.Unit.Kind == "hero");
        if (players.Count == 0) return;
        var heir = players.Keys.Min();
        foreach (var unit in units) if (unit.Owner == owner) unit.Owner = heir;
        // An AI ally steps back in so the MOBA side stays at five heroes.
        if (Mode == GameMode.Moba && units.Count < MaxUnits) SpawnChampion(Catalog.AllyChampions[^1], 0, 1, 3);
        Message = "Seorang pemain meninggalkan pertempuran. / A player left the battle.";
    }

    public bool HasPlayer(int owner) => players.ContainsKey(owner);

    public bool Execute(int owner, GameCommand command)
    {
        if (command.Kind == CommandKind.AdvanceDialogue)
        {
            if (Status != MatchStatus.Playing || !players.ContainsKey(owner) || dialogueKey is null) return false;
            NextLine();
            return true;
        }
        if (command.Kind == CommandKind.BuyItem) return Status == MatchStatus.Playing && players.ContainsKey(owner) && BuyItem(owner, command.TargetId);
        var hero = units.Find(u => u.Owner == owner && u.Kind == "hero" && u.Health > 0);
        if (Status != MatchStatus.Playing || hero is null || !Enum.IsDefined(command.Kind)) return false;
        switch (command.Kind)
        {
            case CommandKind.Move:
            case CommandKind.AttackMove:
            case CommandKind.HoldPosition:
            case CommandKind.Gather:
            case CommandKind.ReturnCargo:
            case CommandKind.Construct:
                return IssueOrder(owner, hero, command);
            case CommandKind.Guard:
                if (guardCooldowns[owner] > 0) return false;
                hero.Health = Math.Min(hero.MaxHealth, hero.Health + hero.MaxHealth * .2);
                hero.GuardTicks = GuardTicks; guardCooldowns[owner] = 12;
                Message = "Tameng Jagat: luka berkurang separuh. / Jagat Guard: damage taken halved.";
                return true;
            case CommandKind.Ultimate:
                if (cooldowns[owner] > 0) return false;
                var power = 140 + (Mode == GameMode.Moba ? heroLevels[owner] : Level) * 15;
                switch (players[owner].Ultimate)
                {
                    case UltimateKind.Chain: ChainLightning(hero, power); cooldowns[owner] = 16; break;
                    case UltimateKind.Leap: Leap(hero, power); cooldowns[owner] = 18; break;
                    default:
                        foreach (var target in units)
                            if (Hostile(target) && Distance(hero, target) < 20 && CanSee(hero, target)) Hit(target, power, 0);
                        hero.Health = Math.Min(hero.MaxHealth, hero.Health + 55);
                        cooldowns[owner] = 18;
                        break;
                }
                if (Mode != GameMode.Adventure && BaseExposed(1) && Distance(hero.X, hero.Z, 87, 87) < 22) EnemyBaseHealth -= 110;
                Message = players[owner].Skill;
                return true;
            case CommandKind.Recruit:
                if (Mode != GameMode.Rts || Gold < 60 || Supplies < 2 || units.Count >= MaxUnits) return false;
                Gold -= 60; Supplies -= 2;
                Spawn(owner, "soldier", 0, 17, 20, 110 + Level * 20, 14 + Level * 3, "#d4b478");
                return true;
            case CommandKind.Build:
                if (Mode != GameMode.Rts || Gold < 100 || units.Count(u => u.Kind == "outpost" && u.Health > 0) >= 4 || units.Count >= MaxUnits || !CanPlaceOutpost(command.X, command.Z)) return false;
                Gold -= 100;
                var site = Spawn(owner, "outpost", 0, command.X, command.Z, 220, 0, "#d4b478");
                site.Health = 44; site.BuildTicks = 0; site.BuildPriority = site.Id;
                RebuildNavigation();
                var builder = units.Where(u => u.Owner == owner && u.Kind == "worker" && u.Health > 0 && u.Order == UnitOrder.Hold)
                    .OrderBy(u => Distance(u, site)).ThenBy(u => u.Id).FirstOrDefault();
                if (builder is not null) AssignConstruction(builder, site);
                Message = "Fondasi ditempatkan. Pekerja diperlukan. / Foundation placed. Worker required.";
                return true;
            case CommandKind.CancelConstruction:
                var cancelled = units.Find(u => u.Id == command.TargetId && u.Owner == owner && u.Kind == "outpost" && u.Health > 0 && u.BuildTicks < ConstructionTicks);
                if (Mode != GameMode.Rts || cancelled is null) return false;
                Gold += ConstructionRefund(cancelled);
                units.Remove(cancelled);
                RebuildNavigation();
                foreach (var assigned in units.Where(u => u.Kind == "worker" && u.Health > 0 && u.Order == UnitOrder.Construct && u.BuildId == cancelled.Id))
                { assigned.Order = UnitOrder.Hold; TryNextConstruction(assigned); }
                Message = "Fondasi dibatalkan; biaya tersisa dikembalikan sebagian. / Foundation cancelled; unused cost partially refunded.";
                return true;
            case CommandKind.PrioritizeConstruction:
                if (Mode != GameMode.Rts) return false;
                var queue = BuildQueue(owner).ToList();
                var position = queue.FindIndex(u => u.Id == command.TargetId);
                if (position <= 0) return false; // Unknown, foreign, finished, or already first.
                queue[position].BuildPriority = --nextPriority;
                Message = "Fondasi didahulukan. / Foundation moved to the front.";
                return true;
            case CommandKind.RecruitWorker:
                if (Mode != GameMode.Rts || Gold < 40 || Supplies < 1 || units.Count >= MaxUnits || units.Count(u => u.Owner == owner && u.Kind == "worker" && u.Health > 0) >= MaxWorkersPerPlayer) return false;
                Gold -= 40; Supplies--;
                var worker = Spawn(owner, "worker", 0, 20, 18, 80, 0, "#a5dec4");
                worker.Order = UnitOrder.Hold;
                return true;
            case CommandKind.Upgrade:
                if (Mode != GameMode.Rts || Gold < 140 || Level >= 4) return false;
                Gold -= 140; Level++; Supplies += 4;
                foreach (var ally in units.Where(u => u.Team == 0 && u.Kind is "hero" or "soldier")) ally.Damage += 6;
                return true;
            default: return false;
        }
    }

    public void Advance()
    {
        if (Status != MatchStatus.Playing) return;
        Tick++;
        foreach (var owner in players.Keys) cooldowns[owner] = Math.Max(0, cooldowns[owner] - StepSeconds);
        if (Tick % 10 == 0) Gold += 3;
        foreach (var owner in players.Keys) guardCooldowns[owner] = Math.Max(0, guardCooldowns[owner] - StepSeconds);
        if (buffTicks[0] > 0) buffTicks[0]--;
        if (buffTicks[1] > 0) buffTicks[1]--;
        if (Tick % 5 == 0) MarkExplored();
        if (Mode == GameMode.Moba && Tick % ChampionGrowthTicks == 0 && championGrowth < MaxChampionGrowth)
        {
            // AI heroes on both sides grow with match time, keeping pace with the player's levels.
            championGrowth++;
            foreach (var champion in units.Where(u => u.Kind == "champion").Concat(respawns.Select(r => r.Unit).Where(u => u.Kind == "champion")))
            { champion.MaxHealth += 15; champion.Damage += 2; if (champion.Health > 0) champion.Health += 15; }
        }
        if (bossTicks > 0 && --bossTicks == 0 && units.Count < MaxUnits) SpawnBoss();
        for (var i = respawns.Count - 1; i >= 0; i--)
        {
            var pending = respawns[i];
            if (--pending.Ticks > 0 || units.Count >= MaxUnits) continue;
            var back = pending.Unit;
            back.Health = back.MaxHealth; back.X = back.TargetX = back.OrderX = back.HomeX; back.Z = back.TargetZ = back.OrderZ = back.HomeZ;
            back.Order = UnitOrder.Auto; back.Route = []; back.RouteIndex = 0; back.AttackCooldown = 0; back.PassedLane = back.Retreating = false; back.LastHitTeam = -1;
            units.Add(back); respawns.RemoveAt(i);
        }
        if (Mode != GameMode.Adventure && Tick % 180 == 0)
        {
            SpawnWave(1);
            if (Mode == GameMode.Moba) SpawnWave(0);
        }
        foreach (var unit in units)
        {
            if (unit.Health <= 0 || unit.Team == 2 || unit.Kind == "outpost") continue;
            if (unit.Kind == "worker") { AdvanceWorker(unit); continue; }
            unit.AttackCooldown -= StepSeconds;
            if (unit.GuardTicks > 0) unit.GuardTicks--;
            if (unit.BoostTicks > 0) unit.BoostTicks--;
            Unit? nearest = null; Unit? nearestTroop = null; var troopDistance = double.MaxValue;
            var nearestDistance = double.MaxValue;
            foreach (var other in units)
            {
                if (other.Team == unit.Team || other.Team == 2 || other.Health <= 0) continue;
                // Only player heroes and healthy enemy hunters pick a fight with the neutral boss, and it answers only them.
                if (other.Team == 3 && unit.Kind != "hero" && !(unit.Hunter && !unit.Retreating && unit.Health > unit.MaxHealth * .6)) continue;
                if (unit.Team == 3 && other.Kind != "hero" && !other.Hunter) continue;
                var distance = Distance(unit, other);
                if (distance < nearestDistance) { nearest = other; nearestDistance = distance; }
                if (unit.Kind == "tower" && other.Kind is not ("hero" or "champion") && distance < troopDistance) { nearestTroop = other; troopDistance = distance; }
            }
            // Towers shoot lane troops before heroes.
            if (nearestTroop is not null && troopDistance <= 14) { nearest = nearestTroop; nearestDistance = troopDistance; }
            var range = unit.Kind switch { "hero" => 13, "tower" => 14, "champion" => 11, "boss" => unit.Team == 3 ? 14 : 9, _ => 6 };
            var inRange = nearest is not null && nearestDistance <= range && CanSee(unit, nearest);
            if (inRange && unit.AttackCooldown <= 0)
            {
                var empowered = unit.Team is 0 or 1 && buffTicks[unit.Team] > 0 && unit.Kind is "hero" or "champion";
                Hit(nearest!, empowered ? unit.Damage * 1.25 : unit.Damage, unit.Team);
                unit.AttackCooldown = unit.Kind switch { "hero" => .65, "champion" => .9, "tower" => 1, _ => 1.2 };
            }
            if (unit.Kind == "tower" || unit.Team == 3) continue; // Stationary.
            if (unit.Kind == "champion")
            {
                if (unit.Health < unit.MaxHealth * .3) unit.Retreating = true;
                else if (unit.Health > unit.MaxHealth * .8) unit.Retreating = false;
            }
            if (unit.Order == UnitOrder.Hold)
            { unit.TargetX = unit.X; unit.TargetZ = unit.Z; }
            else if (unit.Order == UnitOrder.Move)
            { unit.TargetX = unit.OrderX; unit.TargetZ = unit.OrderZ; }
            else if (unit.Order == UnitOrder.AttackMove)
            {
                if (inRange) { unit.TargetX = unit.X; unit.TargetZ = unit.Z; }
                else if (nearest is not null && nearestDistance < Math.Min(24, Sight(unit)) && CanSee(unit, nearest))
                { unit.TargetX = nearest.X; unit.TargetZ = nearest.Z; }
                else { unit.TargetX = unit.OrderX; unit.TargetZ = unit.OrderZ; }
            }
            else if (unit.Retreating) unit.TargetX = unit.TargetZ = unit.Team == 0 ? 13 : 87;
            else if (unit.Kind != "hero")
            {
                if (nearest is not null && nearestDistance < Sight(unit) && (Mode != GameMode.Rts || CanSee(unit, nearest)))
                { unit.TargetX = nearest.X; unit.TargetZ = nearest.Z; }
                else if (Mode != GameMode.Adventure)
                {
                    // MOBA waves follow one of three paths until reaching the enemy shrine.
                    if (Mode == GameMode.Moba && !unit.PassedLane && Distance(unit.X, unit.Z, unit.LaneX, unit.LaneZ) > 3)
                    { unit.TargetX = unit.LaneX; unit.TargetZ = unit.LaneZ; }
                    else { unit.PassedLane = true; unit.TargetX = unit.Team == 0 ? 87 : 13; unit.TargetZ = unit.TargetX; }
                }
                if (inRange) { unit.TargetX = unit.X; unit.TargetZ = unit.Z; }
            }
            Move(unit);
            if (Mode != GameMode.Adventure && BaseExposed(1 - unit.Team) && unit.AttackCooldown <= 0 && Distance(unit.X, unit.Z, unit.Team == 0 ? 87 : 13, unit.Team == 0 ? 87 : 13) < 12)
            {
                if (unit.Team == 0) EnemyBaseHealth -= unit.Damage;
                else BaseHealth -= unit.Damage;
                unit.AttackCooldown = 1;
            }
            if (unit.Kind == "champion" && Distance(unit.X, unit.Z, unit.Team == 0 ? 13 : 87, unit.Team == 0 ? 13 : 87) < 16) unit.Health = Math.Min(unit.MaxHealth, unit.Health + 2);
            if (unit.Kind == "hero")
            {
                if (Distance(unit.X, unit.Z, 13, 13) < 16) unit.Health = Math.Min(unit.MaxHealth, unit.Health + (Mode == GameMode.Moba ? 2 : .8));
                foreach (var relic in units)
                    if (relic.Kind == "relic" && !collected.Contains(relic.Id) && Distance(unit, relic) < 5)
                    { collected.Add(relic.Id); relic.Health = 0; Gold += 80; unit.Health = unit.MaxHealth; Message = "Prasasti ditemukan. / Inscription recovered."; }
                    else if (relic.Kind == "flotsam" && relic.Health > 0 && Distance(unit, relic) < 4)
                    { relic.Health = 0; Gold += 30; Message = "Serpihan kapal ditemukan. / Ship fragment recovered."; }
            }
        }
        foreach (var dead in units.Where(u => u.Health <= 0 && u.Team != 2))
        {
            if (dead.Team == 1) Gold += dead.Kind switch { "boss" => 200, "tower" => 150, "champion" => 60, _ => 20 };
            if (dead.Team == 3)
            {
                bossTicks = BossRespawnTicks;
                if (dead.LastHitTeam == 0) { buffTicks[0] = BuffTicks; Gold += 150; Message = "Restu Naga Basuki: serangan hero +25%. / Naga Basuki's blessing: hero attacks +25%."; }
                else if (dead.LastHitTeam == 1) { buffTicks[1] = BuffTicks; Message = "Musuh merebut Restu Naga Basuki. / The enemy claimed Naga Basuki's blessing."; }
            }
            if (Mode == GameMode.Moba && dead.Team == 1) GrantExperience(dead);
            if (Mode == GameMode.Moba && dead.Kind is "hero" or "champion") respawns.Add(new Pending(dead, RespawnTicks));
            if (dead.Kind == "soldier" && dead.Team == 0) Supplies += 2;
            if (dead.Kind == "worker") Supplies++;
        }
        var destroyedOutpost = units.Any(u => u.Kind == "outpost" && u.Health <= 0);
        units.RemoveAll(u => u.Health <= 0);
        if (destroyedOutpost) RebuildNavigation();
        if (Mode == GameMode.Adventure) AdvanceStory();
        BaseHealth = Math.Max(0, BaseHealth); EnemyBaseHealth = Math.Max(0, EnemyBaseHealth);
        if (BaseHealth <= 0 || (Mode != GameMode.Moba && !units.Exists(u => u.Kind == "hero"))) Status = MatchStatus.Defeat;
        else if (Mode == GameMode.Adventure ? questsDone.Contains(Story.Kalatidha) : EnemyBaseHealth <= 0) Status = MatchStatus.Victory;
    }

    /// <summary>Per-player view. In RTS, enemies outside shared team vision are omitted unless <paramref name="fog"/> is false.</summary>
    public MatchView Snapshot(int owner = 0, bool fog = true)
    {
        var vision = Mode == GameMode.Rts ? Vision() : [];
        var views = new List<UnitView>(units.Count);
        foreach (var u in units)
        {
            if (fog && Mode == GameMode.Rts && u.Team == 1 && !Sees(vision, u.X, u.Z)) continue;
            var site = u.Kind == "outpost";
            views.Add(new UnitView(u.Id, u.Owner, u.Kind, u.Team, u.X, u.Z, u.Health, u.MaxHealth, u.Color, u.Order, u.OrderX, u.OrderZ, u.Cargo,
                u.BuildTicks * 100 / ConstructionTicks, site ? ConstructionRefund(u) : 0, site ? QueuePosition(u) : 0, u.Model));
        }
        return new(Mode, Status, Tick, Gold, Supplies, Mode == GameMode.Moba ? heroLevels.GetValueOrDefault(owner, 1) : Level, Relics, BaseHealth, EnemyBaseHealth, cooldowns.GetValueOrDefault(owner), Message, views.ToArray(),
            (Mode == GameMode.Rts ? NavigationMap.Rts : navigation).Obstacles.ToArray(), resources.Select(r => new ResourceView(r.Id, r.X, r.Z, r.Remaining)).ToArray(), GoldCollected, vision,
            (respawns.Find(r => r.Unit.Owner == owner && r.Unit.Kind == "hero")?.Ticks ?? 0) * StepSeconds, items.GetValueOrDefault(owner), buffTicks[0] * StepSeconds,
            Mode == GameMode.Moba && heroLevels.TryGetValue(owner, out var heroLevel) && heroLevel < MaxHeroLevel ? experience[owner] / (heroLevel * 100d) : 0,
            guardCooldowns.GetValueOrDefault(owner), buffTicks[1] * StepSeconds,
            Mode == GameMode.Adventure ? QuestViews() : null, dialogueKey is null ? "" : $"{dialogueKey}:{dialogueLine}");
    }

    public const double ShrineVision = 24;
    public static double VisionRadius(string kind, bool complete = true) => kind switch { "hero" => 26, "soldier" => 18, "outpost" => complete ? 20 : 8, _ => 14 };

    // Vision is shared by the allied team: the shrine plus every living allied unit and building.
    private VisionView[] Vision()
    {
        var sources = new List<VisionView>(units.Count + 1) { new(13, 13, ShrineVision) };
        foreach (var u in units)
            if (u.Team == 0 && u.Health > 0) sources.Add(new(u.X, u.Z, VisionRadius(u.Kind, u.BuildTicks >= ConstructionTicks) * Atmosphere.SightFactor(Tick)));
        return sources.ToArray();
    }

    // Within radius and not hidden behind a ruin. Outposts are low and never block sight.
    private static bool Sees(VisionView[] vision, double x, double z)
    {
        foreach (var source in vision)
        {
            var dx = x - source.X; var dz = z - source.Z;
            if (dx * dx + dz * dz <= source.Radius * source.Radius && NavigationMap.Rts.ClearSegment(new(source.X, source.Z), new(x, z), 0)) return true;
        }
        return false;
    }

    // How far a unit notices enemies on its own. In RTS this is its vision radius, so no side acts on what it cannot see.
    // Mist and storm shorten it (Atmosphere.SightFactor); the shrine's own light does not change.
    private double Sight(Unit unit) => Mode switch { GameMode.Rts => VisionRadius(unit.Kind), GameMode.Adventure => 24, _ => 30 } * Atmosphere.SightFactor(Tick);

    private static bool Hostile(Unit target) => target.Team is 1 or 3 && target.Health > 0;

    /// <summary>Lightning strikes the nearest visible enemy within 26, then jumps up to five times to the nearest
    /// enemy within 12 of the last one, losing 12% each jump; afterwards the hero runs 60% faster for four seconds.</summary>
    private void ChainLightning(Unit hero, double power)
    {
        Unit? last = null; var struck = new HashSet<int>();
        for (var jump = 0; jump < 6; jump++)
        {
            Unit? next = null; var best = double.MaxValue;
            foreach (var target in units)
            {
                if (!Hostile(target) || struck.Contains(target.Id)) continue;
                var from = last ?? hero; var distance = Distance(from, target);
                if (distance < (last is null ? 26 : 12) && distance < best && CanSee(from, target)) { next = target; best = distance; }
            }
            if (next is null) break;
            Hit(next, power * (1 - .12 * jump), 0); struck.Add(next.Id); last = next;
        }
        hero.BoostTicks = 40;
    }

    /// <summary>The hero flies along a clear line to just short of the nearest visible enemy within 30 (or stays put),
    /// then a shockwave hits every enemy within 14 for 110% power and staggers them; the hero takes half damage for four seconds.</summary>
    private void Leap(Unit hero, double power)
    {
        Unit? target = null; var best = double.MaxValue;
        foreach (var candidate in units)
            if (Hostile(candidate) && Distance(hero, candidate) is var distance && distance < 30 && distance < best && CanSee(hero, candidate)) { target = candidate; best = distance; }
        if (target is not null && best > 2)
        {
            var share = (best - 2) / best;
            hero.X += (target.X - hero.X) * share; hero.Z += (target.Z - hero.Z) * share;
            hero.TargetX = hero.X; hero.TargetZ = hero.Z; hero.Route = []; hero.RouteIndex = 0;
            if (hero.Order == UnitOrder.Move) { hero.OrderX = hero.X; hero.OrderZ = hero.Z; }
        }
        foreach (var victim in units)
            if (Hostile(victim) && Distance(hero, victim) < 14 && CanSee(hero, victim)) { Hit(victim, power * 1.1, 0); victim.AttackCooldown = Math.Max(victim.AttackCooldown, 1.5); }
        hero.GuardTicks = GuardTicks;
    }

    private static void Hit(Unit target, double damage, int team)
    {
        if (target.Invulnerable) return;
        target.Health -= target.GuardTicks > 0 ? damage * .5 : damage;
        target.LastHitTeam = team;
    }

    private void GrantExperience(Unit dead)
    {
        var amount = dead.Kind switch { "tower" => 100, "champion" => 60, _ => 20 };
        foreach (var hero in units)
        {
            if (hero.Kind != "hero" || hero.Health <= 0 || (dead.Kind != "tower" && Distance(hero, dead) > 25)) continue;
            var level = heroLevels[hero.Owner];
            if (level >= MaxHeroLevel) continue;
            experience[hero.Owner] += amount;
            while (level < MaxHeroLevel && experience[hero.Owner] >= level * 100)
            {
                experience[hero.Owner] -= level * 100; level++;
                hero.MaxHealth += 25; hero.Health += 25; hero.Damage += 4;
                Message = $"Hero naik ke level {level}. / Hero reached level {level}.";
            }
            if (level >= MaxHeroLevel) experience[hero.Owner] = 0;
            heroLevels[hero.Owner] = level;
        }
    }

    private static bool Unfinished(Unit u) => u.Kind == "outpost" && u.Health > 0 && u.BuildTicks < ConstructionTicks;

    // Placement order unless the owner moved a site to the front.
    private IEnumerable<Unit> BuildQueue(int owner) => units.Where(u => u.Owner == owner && Unfinished(u)).OrderBy(u => u.BuildPriority).ThenBy(u => u.Id);

    private int QueuePosition(Unit site)
    {
        if (!Unfinished(site)) return 0;
        var position = 1;
        foreach (var other in units)
            if (other != site && other.Owner == site.Owner && Unfinished(other)
                && (other.BuildPriority < site.BuildPriority || (other.BuildPriority == site.BuildPriority && other.Id < site.Id))) position++;
        return position;
    }

    public bool CanPlaceOutpost(double x, double z)
    {
        if (Mode != GameMode.Rts || !double.IsFinite(x) || !double.IsFinite(z) || x < 7 || x > 93 || z < 7 || z > 93 || !IsExplored(x, z)) return false;
        if (Distance(x, z, 13, 13) < 13 || Distance(x, z, 13, 13) > 55) return false;
        if (navigation.Obstacles.Any(o => Math.Abs(x - o.X) <= o.Width / 2 + 5 && Math.Abs(z - o.Z) <= o.Depth / 2 + 5)) return false;
        if (resources.Any(r => Distance(x, z, r.X, r.Z) < 7)) return false;
        // Reserve recruitment and second-player arrival points, including navigation clearance.
        MapPoint[] spawns = [new(17, 20), new(20, 18), new(29, 24)];
        if (spawns.Any(p => Math.Abs(x - p.X) <= 5 && Math.Abs(z - p.Z) <= 5)) return false;
        return !units.Any(u => u.Health > 0 && Math.Abs(x - u.X) <= (u.Kind == "outpost" ? 8 : 5) && Math.Abs(z - u.Z) <= (u.Kind == "outpost" ? 8 : 5));
    }

    private void AdvanceWorker(Unit worker)
    {
        if (worker.Order == UnitOrder.Construct)
        {
            var site = units.Find(u => u.Id == worker.BuildId && u.Kind == "outpost" && u.Owner == worker.Owner && u.Health > 0 && u.BuildTicks < ConstructionTicks);
            if (site is null) { worker.Order = UnitOrder.Hold; TryNextConstruction(worker); return; }
            var access = Approach(new(worker.X, worker.Z), new(site.X, site.Z));
            worker.TargetX = access.X; worker.TargetZ = access.Z;
            if (!navigation.IsWalkable(access)) return;
            if (Distance(worker.X, worker.Z, access.X, access.Z) > .2) { Move(worker); return; }
            // Several workers may assist, but progress is capped at one step per match tick.
            if (site.LastBuildTick == Tick) return;
            site.LastBuildTick = Tick;
            site.BuildTicks++;
            site.Health = Math.Min(site.MaxHealth, site.Health + 176d / ConstructionTicks);
            if (site.BuildTicks == ConstructionTicks)
            {
                worker.Order = UnitOrder.Hold;
                TryNextConstruction(worker);
                Message = "Pos siap menerima setoran. / Outpost ready for deposits.";
            }
            return;
        }
        if (worker.Order == UnitOrder.Move)
        { worker.TargetX = worker.OrderX; worker.TargetZ = worker.OrderZ; Move(worker); return; }
        if (worker.Order != UnitOrder.Gather) return;
        var node = resources.Find(r => r.Id == worker.ResourceId);
        if (worker.Cargo > 0)
        {
            // Recompute the nearest surviving depot, so a destroyed outpost cannot strand cargo.
            var depot = new MapPoint(13, 13);
            var nearest = Distance(worker.X, worker.Z, depot.X, depot.Z);
            foreach (var outpost in units.Where(u => u.Kind == "outpost" && u.Health > 0 && u.BuildTicks == ConstructionTicks))
            {
                var access = Approach(new(worker.X, worker.Z), new(outpost.X, outpost.Z));
                var distance = Distance(worker.X, worker.Z, access.X, access.Z);
                if (distance < nearest && navigation.IsWalkable(access)) { nearest = distance; depot = access; }
            }
            worker.TargetX = depot.X; worker.TargetZ = depot.Z;
            if (nearest <= 3)
            {
                Gold += worker.Cargo; GoldCollected += worker.Cargo; worker.Cargo = 0;
                if (node is null || node.Remaining == 0) worker.Order = UnitOrder.Hold;
            }
            else Move(worker);
            return;
        }
        if (node is null || node.Remaining == 0) { worker.Order = UnitOrder.Hold; return; }
        worker.TargetX = node.X; worker.TargetZ = node.Z;
        if (Distance(worker.X, worker.Z, node.X, node.Z) > 2) { Move(worker); return; }
        if (++worker.GatherTicks < 20) return;
        worker.GatherTicks = 0;
        worker.Cargo = Math.Min(20, node.Remaining);
        node.Remaining -= worker.Cargo;
    }

    private bool IssueOrder(int owner, Unit hero, GameCommand command)
    {
        if (Mode != GameMode.Rts && (command.Kind != CommandKind.Move || command.UnitIds is not null)) return false;
        var ids = command.UnitIds ?? [hero.Id];
        if (ids.Length is 0 or > MaxSelection || ids.Distinct().Count() != ids.Length) return false;
        var selected = new List<Unit>(ids.Length);
        foreach (var id in ids)
        {
            var unit = units.Find(u => u.Id == id && u.Owner == owner && u.Team == 0 && u.Health > 0 && (u.Kind is "hero" or "soldier" or "worker"));
            if (unit is null) return false; // All-or-nothing validation prevents partial foreign-unit commands.
            selected.Add(unit);
        }
        if (command.Kind == CommandKind.Construct)
        {
            var site = units.Find(u => u.Id == command.TargetId && u.Kind == "outpost" && u.Owner == owner && u.Health > 0 && u.BuildTicks < ConstructionTicks);
            if (site is null || selected.Any(u => u.Kind != "worker" || !navigation.IsWalkable(Approach(new(u.X, u.Z), new(site.X, site.Z))))) return false;
            foreach (var worker in selected) AssignConstruction(worker, site);
            return true;
        }
        if (command.Kind == CommandKind.ReturnCargo)
        {
            if (selected.Any(u => u.Kind != "worker") || !selected.Any(u => u.Cargo > 0)) return false;
            foreach (var unit in selected) { unit.Order = UnitOrder.Gather; unit.ResourceId = 0; unit.GatherTicks = 0; unit.Route = []; }
            return true;
        }
        if (command.Kind == CommandKind.Gather)
        {
            var resource = resources.Find(r => r.Id == command.TargetId && r.Remaining > 0);
            if (resource is null || selected.Any(u => u.Kind != "worker" || navigation.FindPath(new(u.X, u.Z), new(resource.X, resource.Z)).Length == 0)) return false;
            foreach (var unit in selected) { unit.Order = UnitOrder.Gather; unit.ResourceId = resource.Id; unit.GatherTicks = 0; unit.Route = []; }
            return true;
        }
        if (command.Kind == CommandKind.AttackMove && selected.Any(u => u.Kind == "worker")) return false;
        if (command.Kind == CommandKind.HoldPosition)
        {
            foreach (var unit in selected) { unit.Order = UnitOrder.Hold; unit.OrderX = unit.TargetX = unit.X; unit.OrderZ = unit.TargetZ = unit.Z; unit.Route = []; unit.GatherTicks = 0; }
            return true;
        }
        if (!double.IsFinite(command.X) || !double.IsFinite(command.Z)) return false;
        var destination = new MapPoint(Math.Clamp(command.X, 4, 96), Math.Clamp(command.Z, 4, 96));
        if (!navigation.IsWalkable(destination)) return false;
        var plans = new List<(Unit Unit, MapPoint Goal, MapPoint[] Route)>();
        // Stable ID ordering keeps the formation identical regardless of UI selection order.
        selected.Sort((a, b) => a.Id.CompareTo(b.Id));
        var columns = (int)Math.Ceiling(Math.Sqrt(selected.Count));
        for (var i = 0; i < selected.Count; i++)
        {
            var goal = new MapPoint(destination.X + (i % columns - (columns - 1) / 2d) * 3,
                destination.Z + (i / columns - (Math.Ceiling(selected.Count / (double)columns) - 1) / 2d) * 3);
            if (!navigation.IsWalkable(goal)) goal = destination;
            var route = navigation.FindPath(new(selected[i].X, selected[i].Z), goal);
            if (route.Length == 0) return false;
            plans.Add((selected[i], goal, route));
        }
        foreach (var (unit, goal, route) in plans)
        {
            unit.Order = command.Kind == CommandKind.AttackMove ? UnitOrder.AttackMove : UnitOrder.Move;
            unit.OrderX = unit.TargetX = unit.RouteX = goal.X; unit.OrderZ = unit.TargetZ = unit.RouteZ = goal.Z;
            unit.Route = route; unit.RouteIndex = 0;
            unit.GatherTicks = 0;
        }
        return true;
    }

    // Low outposts block walking, not ranged attacks; otherwise attackers cannot hit their center.
    private bool CanSee(Unit a, Unit b) => (Mode == GameMode.Rts ? NavigationMap.Rts : navigation).ClearSegment(new(a.X, a.Z), new(b.X, b.Z), 0);

    private void RebuildNavigation()
    {
        navigation = new NavigationMap(NavigationMap.Rts.Obstacles.Concat(
            units.Where(u => u.Kind == "outpost" && u.Health > 0).Select(u => new ObstacleView(u.X, u.Z, 6, 6))));
        foreach (var unit in units) { unit.Route = []; unit.RouteIndex = 0; }
    }

    private static void AssignConstruction(Unit worker, Unit site)
    {
        worker.Order = UnitOrder.Construct; worker.BuildId = site.Id;
        worker.OrderX = site.X; worker.OrderZ = site.Z;
        worker.Route = []; worker.RouteIndex = 0; worker.GatherTicks = 0;
    }

    private static int ConstructionRefund(Unit site) => 75 * (ConstructionTicks - site.BuildTicks) / ConstructionTicks;

    private void TryNextConstruction(Unit worker)
    {
        // Follow the owner's queue. Skip staffed sites so spare builders spread out.
        foreach (var site in BuildQueue(worker.Owner))
        {
            if (units.Any(u => u.Kind == "worker" && u.Health > 0 && u.Order == UnitOrder.Construct && u.BuildId == site.Id)) continue;
            var access = Approach(new(worker.X, worker.Z), new(site.X, site.Z));
            if (!navigation.IsWalkable(access)) continue;
            AssignConstruction(worker, site);
            return;
        }
    }

    private MapPoint Approach(MapPoint from, MapPoint target)
    {
        if (navigation.IsWalkable(target)) return target;
        var post = units.Find(u => u.Kind == "outpost" && u.Health > 0 && Math.Abs(target.X - u.X) <= 5 && Math.Abs(target.Z - u.Z) <= 5);
        if (post is null) return target;
        const double edge = 5.1; // Three-unit footprint plus two-unit walking clearance.
        // Approach a face rather than a corner: melee range is six units from the center.
        var x = Math.Clamp(from.X, post.X - 2, post.X + 2);
        var z = Math.Clamp(from.Z, post.Z - 2, post.Z + 2);
        MapPoint[] candidates = [new(post.X - edge, z), new(post.X + edge, z), new(x, post.Z - edge), new(x, post.Z + edge)];
        foreach (var candidate in candidates.OrderBy(p => Distance(from.X, from.Z, p.X, p.Z)))
            if (navigation.FindPath(from, candidate).Length > 0) return candidate;
        return target;
    }

    private void SpawnWave(int team)
    {
        for (var lane = 0; lane < (Mode == GameMode.Moba ? 3 : 2); lane++)
        {
            if (units.Count >= MaxUnits) return;
            var unit = Spawn(-1, "soldier", team, team == 0 ? 24 : 76, (team == 0 ? 24 : 76) + lane * 2, 75, 9, team == 0 ? "#d4b478" : "#d17791");
            SetLane(unit, lane);
        }
    }
    private Unit Spawn(int owner, string kind, int team, double x, double z, double health, int damage, string color)
    {
        var unit = new Unit { Id = ++nextId, Owner = owner, Kind = kind, Team = team, X = x, Z = z, TargetX = x, TargetZ = z, OrderX = x, OrderZ = z, Health = health, MaxHealth = health, Damage = damage, Color = color };
        units.Add(unit); return unit;
    }
    private void Move(Unit unit)
    {
        var from = new MapPoint(unit.X, unit.Z); var goal = Approach(from, new(unit.TargetX, unit.TargetZ));
        if (!navigation.IsWalkable(goal)) return;
        var waypoint = goal;
        if (!navigation.ClearSegment(from, goal))
        {
            if (unit.RouteIndex >= unit.Route.Length || Distance(unit.RouteX, unit.RouteZ, goal.X, goal.Z) > 3)
            {
                unit.Route = navigation.FindPath(from, goal); unit.RouteIndex = 0; unit.RouteX = goal.X; unit.RouteZ = goal.Z;
            }
            // Reach the actual corner before advancing; skipping within .1 can cut across a padded footprint.
            while (unit.RouteIndex < unit.Route.Length && Distance(unit.X, unit.Z, unit.Route[unit.RouteIndex].X, unit.Route[unit.RouteIndex].Z) < 1e-6) unit.RouteIndex++;
            if (unit.RouteIndex >= unit.Route.Length) return;
            waypoint = unit.Route[unit.RouteIndex];
        }
        else unit.Route = [];
        var dx = waypoint.X - unit.X; var dz = waypoint.Z - unit.Z;
        var distance = Math.Sqrt(dx * dx + dz * dz);
        var speed = unit.Kind switch { "hero" => 12, "champion" => 8, "worker" => 7, "boss" => 3, _ => 5 } * (1 + unit.SpeedBonus + (unit.BoostTicks > 0 ? .6 : 0)) * StepSeconds;
        if (distance < .1) { unit.X = waypoint.X; unit.Z = waypoint.Z; return; }
        var step = Math.Min(speed, distance) / distance;
        var next = new MapPoint(unit.X + dx * step, unit.Z + dz * step);
        if (navigation.ClearSegment(from, next)) { unit.X = next.X; unit.Z = next.Z; }
    }
    private static double Distance(Unit a, Unit b) => Distance(a.X, a.Z, b.X, b.Z);
    private static double Distance(double x, double z, double a, double b) => Math.Sqrt((x - a) * (x - a) + (z - b) * (z - b));
    private sealed class Unit
    {
        public int Id, Owner, Team, Damage;
        public string Kind = "", Color = "", Model = "";
        public double X, Z, TargetX, TargetZ, Health, MaxHealth, AttackCooldown, LaneX, LaneZ, OrderX, OrderZ, RouteX, RouteZ, HomeX, HomeZ, SpeedBonus;
        public int LastHitTeam = -1, GuardTicks, BoostTicks;
        public bool Retreating, Hunter, Invulnerable;
        public UnitOrder Order;
        public MapPoint[] Route = [];
        public int RouteIndex;
        public int Cargo, ResourceId, GatherTicks;
        public int BuildId, BuildTicks = ConstructionTicks;
        public long LastBuildTick = -1, BuildPriority;
        public bool PassedLane;
    }
    private sealed class Pending(Unit unit, int ticks)
    {
        public Unit Unit { get; } = unit;
        public int Ticks { get; set; } = ticks;
    }
    private sealed class ResourceNode(int id, double x, double z, int remaining)
    {
        public int Id { get; } = id;
        public double X { get; } = x;
        public double Z { get; } = z;
        public int Remaining { get; set; } = remaining;
    }
}
