namespace BattleNusantara.Core;

// Neutral life: jungle camps of creeps that guard loot, and wildlife with its own temperament.
public sealed partial class Match
{
    /// <summary>Creeps notice player units this close, and chase them no further than this from their camp.</summary>
    public const double CreepAggro = 7, CreepLeash = 16, CampAlertRadius = 14;
    /// <summary>Loot lying on the ground at once; the oldest disappears beyond this.</summary>
    public const int MaxLootOnGround = 10, MaxTreasures = 16, WildRespawnTicks = 900;
    private readonly List<Camp> camps = new();
    private readonly List<PendingWild> wildRespawns = new();
    private readonly Dictionary<int, List<string>> treasures = new();

    private sealed class Camp(CampDefinition definition)
    {
        public CampDefinition Definition { get; } = definition;
        public int RespawnTicks, Clears, AlertTicks;
    }

    private sealed class PendingWild(string model, double x, double z, int ticks)
    {
        public string Model { get; } = model;
        public double X { get; } = x;
        public double Z { get; } = z;
        public int Ticks { get; set; } = ticks;
    }

    /// <summary>Test hook: places an animal at a spot, so behaviours can be checked without searching for one.</summary>
    internal int PlaceWildlife(string model, double x, double z) => SpawnWild(model, x, z).Id;

    /// <summary>Test hook: gives a worker a load of gold, as if it had just mined.</summary>
    internal void LoadCargo(int workerId, int gold) { if (units.Find(u => u.Id == workerId && u.Kind == "worker") is { } worker) worker.Cargo = gold; }

    /// <summary>Loot the player's hero has picked up, newest last.</summary>
    public IReadOnlyList<string> Treasures(int owner) => treasures.TryGetValue(owner, out var list) ? list : [];

    private void SetupNeutrals()
    {
        foreach (var definition in Map.Camps)
        {
            var camp = new Camp(definition);
            camps.Add(camp);
            SpawnCamp(camp);
        }
        SpawnWildlife();
    }

    private void SpawnCamp(Camp camp)
    {
        var members = Catalog.CampCreeps(camp.Definition.Tier);
        for (var i = 0; i < members.Length && units.Count < MaxUnits; i++)
        {
            var definition = Catalog.Creep(members[i])!;
            // The strongest creep stands in the middle; the others flank it.
            var angle = i * 2.0944 + camp.Definition.Id;
            var radius = i == 1 ? 0 : 2.6;
            var x = camp.Definition.X + Math.Cos(angle) * radius; var z = camp.Definition.Z + Math.Sin(angle) * radius;
            var creep = Spawn(-1, "creep", 3, x, z, definition.Health, definition.Damage, CampColor(camp.Definition.Tier));
            creep.Model = definition.Model; creep.CampId = camp.Definition.Id; creep.HomeX = x; creep.HomeZ = z;
        }
    }

    private static string CampColor(CampTier tier) => tier switch { CampTier.Lemah => "#b8c27a", CampTier.Sedang => "#e0a85a", CampTier.Kuat => "#e07a5a", _ => "#ff5a3a" };

    /// <summary>Places wildlife in the map's regions, from the match seed: one or two animals per suitable region.</summary>
    private void SpawnWildlife()
    {
        var state = (uint)Seed ^ 0x85EBCA6Bu;
        uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        foreach (var region in Map.Regions)
        {
            var kinds = Catalog.Wildlife.Where(w => w.Biomes.Contains(region.Biome)).ToArray();
            if (kinds.Length == 0) continue;
            var count = 1 + (int)(Next() % 2);
            for (var k = 0; k < count && units.Count < MaxUnits - 20; k++)
            {
                var kind = kinds[Next() % (uint)kinds.Length];
                if (kind.Temperament == Temperament.Thief && Mode != GameMode.Rts) kind = Catalog.Wild("kancil")!;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    var angle = Next() % 628 / 100.0; var distance = region.Radius * .55 * (Next() % 100 / 100.0);
                    var x = region.X + Math.Cos(angle) * distance; var z = region.Z + Math.Sin(angle) * distance;
                    if (!WildSpotIsFree(x, z)) continue;
                    SpawnWild(kind.Model, x, z);
                    break;
                }
            }
        }
    }

    /// <summary>Open ground away from bases, camps, mines and obstacles.</summary>
    private bool WildSpotIsFree(double x, double z) =>
        x >= MapLayout.Margin + 3 && x <= Layout.Max - 3 && z >= MapLayout.Margin + 3 && z <= Layout.Max - 3
        && navigation.IsWalkable(new(x, z))
        && Distance(x, z, Layout.AllyBase, Layout.AllyBase) > 20 && Distance(x, z, Layout.EnemyBase, Layout.EnemyBase) > 20
        && Map.Camps.All(c => Distance(x, z, c.X, c.Z) > 10) && resources.All(r => Distance(x, z, r.X, r.Z) > 6);

    private Unit SpawnWild(string model, double x, double z)
    {
        var definition = Catalog.Wild(model)!;
        var animal = Spawn(-1, "wild", 3, x, z, definition.Health, definition.Damage, "#c8b48a");
        animal.Model = model; animal.HomeX = x; animal.HomeZ = z;
        return animal;
    }

    private void AdvanceCamps()
    {
        foreach (var camp in camps)
        {
            if (camp.AlertTicks > 0) camp.AlertTicks--;
            if (camp.RespawnTicks <= 0) continue;
            // A camp returns only when no player stands in it, so it never appears on top of someone.
            if (--camp.RespawnTicks > 0) continue;
            if (units.Any(u => u.Team == 0 && u.Owner >= 0 && Distance(u.X, u.Z, camp.Definition.X, camp.Definition.Z) < 12)) { camp.RespawnTicks = 50; continue; }
            SpawnCamp(camp);
        }
        for (var i = wildRespawns.Count - 1; i >= 0; i--)
        {
            var pending = wildRespawns[i];
            if (--pending.Ticks > 0 || units.Count >= MaxUnits) continue;
            SpawnWild(pending.Model, pending.X, pending.Z);
            wildRespawns.RemoveAt(i);
        }
    }

    /// <summary>The player side a neutral may fight: player-owned team 0 units, plus any unit of a team that hit it.</summary>
    private static bool PlayerUnit(Unit u) => u.Team == 0 && u.Owner >= 0 && u.Health > 0 && u.Kind is "hero" or "soldier" or "worker";

    private void AdvanceNeutral(Unit unit)
    {
        if (unit.ProvokedTicks > 0) unit.ProvokedTicks--;
        if (unit.Kind == "creep") AdvanceCreep(unit);
        else AdvanceWild(unit);
    }

    private void AdvanceCreep(Unit creep)
    {
        var camp = camps.Find(c => c.Definition.Id == creep.CampId);
        var definition = Catalog.Creep(creep.Model)!;
        var fromHome = Distance(creep.X, creep.Z, creep.HomeX, creep.HomeZ);
        if (creep.Retreating)
        {
            // Leashed: walk home, healing on the way, and fight again only after arriving.
            creep.Health = Math.Min(creep.MaxHealth, creep.Health + creep.MaxHealth * .02);
            if (fromHome < 1) { creep.Retreating = false; creep.Health = creep.MaxHealth; return; }
            creep.TargetX = creep.HomeX; creep.TargetZ = creep.HomeZ; Move(creep);
            return;
        }
        if (fromHome > CreepLeash) { creep.Retreating = true; return; }
        var alerted = camp is { AlertTicks: > 0 } || creep.ProvokedTicks > 0;
        Unit? target = null; var best = double.MaxValue;
        foreach (var other in units)
        {
            if (!PlayerUnit(other) && !(creep.ProvokedTicks > 0 && other.Team == creep.LastHitTeam && other.Team != 3 && other.Health > 0 && other.Kind != "outpost")) continue;
            var distance = Distance(creep, other);
            var reachable = distance <= CreepAggro || (alerted && Distance(other.X, other.Z, camp?.Definition.X ?? creep.HomeX, camp?.Definition.Z ?? creep.HomeZ) <= CampAlertRadius);
            if (reachable && distance < best && CanSee(creep, other)) { target = other; best = distance; }
        }
        if (target is null)
        {
            // Idle: drift back to the post and slowly recover.
            creep.Health = Math.Min(creep.MaxHealth, creep.Health + creep.MaxHealth * .002);
            if (fromHome > .5) { creep.TargetX = creep.HomeX; creep.TargetZ = creep.HomeZ; Move(creep); }
            return;
        }
        if (camp is not null) camp.AlertTicks = 40;
        if (best <= definition.Range)
        {
            if (creep.AttackCooldown <= 0) { Hit(target, creep.Damage, 3); creep.AttackCooldown = definition.Model == "banaspati" ? 1.4 : 1.1; }
            creep.TargetX = creep.X; creep.TargetZ = creep.Z;
            return;
        }
        creep.TargetX = target.X; creep.TargetZ = target.Z; Move(creep);
    }

    private void AdvanceWild(Unit animal)
    {
        var definition = Catalog.Wild(animal.Model)!;
        var fromHome = Distance(animal.X, animal.Z, animal.HomeX, animal.HomeZ);
        // The nearest unit of either army that is close enough to matter.
        Unit? threat = null; var best = double.MaxValue;
        foreach (var other in units)
        {
            if (other.Team is not (0 or 1) || other.Health <= 0 || other.Kind is "outpost" or "tower") continue;
            var distance = Distance(animal, other);
            if (distance < best) { threat = other; best = distance; }
        }
        var provoked = animal.ProvokedTicks > 0;
        switch (definition.Temperament)
        {
            case Temperament.Timid:
                if (threat is not null && best < 9) Flee(animal, threat, definition);
                else Wander(animal, definition, fromHome);
                return;
            case Temperament.Calm:
                if (provoked && threat is not null && best < 12) { Charge(animal, threat, best, definition); return; }
                Wander(animal, definition, fromHome);
                return;
            case Temperament.Territorial:
                if (fromHome > 13) { animal.TargetX = animal.HomeX; animal.TargetZ = animal.HomeZ; Move(animal); return; }
                if (threat is not null && (best < 5 || (provoked && best < 12))) { Charge(animal, threat, best, definition); return; }
                Wander(animal, definition, fromHome);
                return;
            case Temperament.Thief:
                StealOrFlee(animal, definition, threat, best, fromHome);
                return;
        }
    }

    private void Charge(Unit animal, Unit target, double distance, WildlifeDefinition definition)
    {
        if (distance <= 3)
        {
            if (animal.AttackCooldown <= 0) { Hit(target, animal.Damage, 3); animal.AttackCooldown = 1.2; }
            animal.TargetX = animal.X; animal.TargetZ = animal.Z;
            return;
        }
        animal.TargetX = target.X; animal.TargetZ = target.Z; MoveAt(animal, definition.Speed);
    }

    private void Flee(Unit animal, Unit threat, WildlifeDefinition definition)
    {
        var dx = animal.X - threat.X; var dz = animal.Z - threat.Z;
        var length = Math.Max(.1, Math.Sqrt(dx * dx + dz * dz));
        var x = Math.Clamp(animal.X + dx / length * 6, animal.HomeX - 14, animal.HomeX + 14);
        var z = Math.Clamp(animal.Z + dz / length * 6, animal.HomeZ - 14, animal.HomeZ + 14);
        var goal = new MapPoint(Math.Clamp(x, MapLayout.Margin, Layout.Max), Math.Clamp(z, MapLayout.Margin, Layout.Max));
        if (!navigation.IsWalkable(goal)) goal = new MapPoint(animal.HomeX, animal.HomeZ);
        animal.TargetX = goal.X; animal.TargetZ = goal.Z; MoveAt(animal, definition.Speed * 1.4);
    }

    /// <summary>A slow walk to a new spot near home every few seconds, chosen from the seed and the tick.</summary>
    private void Wander(Unit animal, WildlifeDefinition definition, double fromHome)
    {
        var phase = (Tick + animal.Id * 37) / 60;
        if ((Tick + animal.Id * 37) % 60 == 0 || fromHome > 10)
        {
            var hash = (uint)(Seed ^ (animal.Id * 73856093) ^ (int)(phase * 19349663));
            hash ^= hash << 13; hash ^= hash >> 17; hash ^= hash << 5;
            var angle = hash % 628 / 100.0; var radius = 2 + hash % 600 / 100.0;
            var goal = new MapPoint(Math.Clamp(animal.HomeX + Math.Cos(angle) * radius, MapLayout.Margin, Layout.Max), Math.Clamp(animal.HomeZ + Math.Sin(angle) * radius, MapLayout.Margin, Layout.Max));
            if (fromHome > 10 || navigation.IsWalkable(goal)) { animal.TargetX = fromHome > 10 ? animal.HomeX : goal.X; animal.TargetZ = fromHome > 10 ? animal.HomeZ : goal.Z; }
        }
        if (Distance(animal.X, animal.Z, animal.TargetX, animal.TargetZ) > .3) MoveAt(animal, definition.Speed * .35);
    }

    /// <summary>A thieving macaque snatches the load of a worker carrying gold and runs for home; killing it returns the gold.
    /// If it reaches home and stays unharmed for thirty seconds, the gold is lost.</summary>
    private void StealOrFlee(Unit thief, WildlifeDefinition definition, Unit? threat, double threatDistance, double fromHome)
    {
        if (thief.Cargo > 0)
        {
            if (fromHome > 1) { thief.TargetX = thief.HomeX; thief.TargetZ = thief.HomeZ; MoveAt(thief, definition.Speed); return; }
            if (++thief.GatherTicks >= 300) { thief.Cargo = 0; thief.GatherTicks = 0; }
            return;
        }
        var mark = units.Where(u => u.Kind == "worker" && u.Team == 0 && u.Cargo > 0 && u.Health > 0 && Distance(u, thief) < 14)
            .OrderBy(u => Distance(u, thief)).ThenBy(u => u.Id).FirstOrDefault();
        if (mark is not null)
        {
            if (Distance(mark, thief) <= 2)
            {
                thief.Cargo = mark.Cargo; mark.Cargo = 0; thief.GatherTicks = 0;
                Message = $"Kera pencuri merampas {thief.Cargo} emas! / A thieving macaque snatched {thief.Cargo} gold!";
                return;
            }
            thief.TargetX = mark.X; thief.TargetZ = mark.Z; MoveAt(thief, definition.Speed);
            return;
        }
        if (threat is not null && threatDistance < 7 && threat.Kind != "worker") Flee(thief, threat, definition);
        else Wander(thief, definition, fromHome);
    }

    /// <summary>Moves at a given speed (units per second) by borrowing the shared movement code.</summary>
    private void MoveAt(Unit unit, double speed)
    {
        unit.SpeedOverride = speed;
        Move(unit);
        unit.SpeedOverride = 0;
    }

    private void OnNeutralDeath(Unit dead)
    {
        var byPlayers = dead.LastHitTeam == 0;
        if (dead.Kind == "wild")
        {
            var definition = Catalog.Wild(dead.Model)!;
            if (byPlayers) Gold += definition.Gold + dead.Cargo;
            if (byPlayers && dead.Cargo > 0) Message = $"Emas curian kembali: {dead.Cargo}. / Stolen gold recovered: {dead.Cargo}.";
            wildRespawns.Add(new PendingWild(dead.Model, dead.HomeX, dead.HomeZ, WildRespawnTicks));
            return;
        }
        var creep = Catalog.Creep(dead.Model)!;
        if (byPlayers)
        {
            Gold += creep.Gold;
            if (Mode == GameMode.Moba) GrantExperience(dead);
        }
        var camp = camps.Find(c => c.Definition.Id == dead.CampId);
        // A camp is cleared once, even when one blow fells the whole camp in the same tick: the first of those deaths
        // starts the respawn timer, and the others see it running.
        if (camp is null || camp.RespawnTicks > 0 || units.Any(u => u != dead && u.Kind == "creep" && u.CampId == camp.Definition.Id && u.Health > 0)) return;
        camp.Clears++;
        camp.RespawnTicks = Catalog.CampRespawnTicks(camp.Definition.Tier);
        camp.AlertTicks = 0;
        if (!byPlayers) return;
        DropLoot(camp);
        Message = $"Kamp {TierName(camp.Definition.Tier).Id} ditaklukkan! / {TierName(camp.Definition.Tier).En} camp cleared!";
    }

    private static (string Id, string En) TierName(CampTier tier) => tier switch
    {
        CampTier.Lemah => ("lemah", "Weak"), CampTier.Sedang => ("sedang", "Medium"), CampTier.Kuat => ("kuat", "Strong"), _ => ("purba", "Ancient")
    };

    /// <summary>What a cleared camp leaves, decided from the seed, the camp and how often it was cleared, so every
    /// client and a resumed save agree. Stronger camps drop rarer things.</summary>
    public static string[] CampDrops(int seed, int campId, int clears, CampTier tier)
    {
        var state = (uint)(seed ^ campId * 7919 ^ clears * 104729) | 1;
        uint Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; }
        string Pick(LootRarity rarity)
        {
            var options = Catalog.Loot.Where(l => l.Rarity == rarity).ToArray();
            return options[Next() % (uint)options.Length].Id;
        }
        var drops = new List<string>();
        switch (tier)
        {
            case CampTier.Lemah:
                if (Next() % 100 < 50) drops.Add(Pick(LootRarity.Minor));
                break;
            case CampTier.Sedang:
                drops.Add(Pick(LootRarity.Minor));
                if (Next() % 100 < 30) drops.Add(Pick(LootRarity.Major));
                break;
            case CampTier.Kuat:
                drops.Add(Pick(LootRarity.Major));
                if (Next() % 100 < 50) drops.Add(Pick(LootRarity.Minor));
                break;
            default:
                drops.Add(Pick(LootRarity.Legendary));
                drops.Add(Pick(LootRarity.Major));
                drops.Add("peti");
                break;
        }
        return drops.ToArray();
    }

    private void DropLoot(Camp camp)
    {
        var drops = CampDrops(Seed, camp.Definition.Id, camp.Clears, camp.Definition.Tier);
        for (var i = 0; i < drops.Length; i++)
        {
            while (units.Count(u => u.Kind == "loot") >= MaxLootOnGround) units.Remove(units.First(u => u.Kind == "loot"));
            if (units.Count >= MaxUnits) return;
            var angle = i * 2.4 + camp.Clears;
            var item = Catalog.LootItem(drops[i])!;
            var loot = Spawn(-1, "loot", 2, camp.Definition.X + Math.Cos(angle) * 1.5, camp.Definition.Z + Math.Sin(angle) * 1.5, 1, 0, RarityColor(item.Rarity));
            loot.Model = item.Id;
        }
    }

    private static string RarityColor(LootRarity rarity) => rarity switch
    {
        LootRarity.Minor => "#9ad37a", LootRarity.Major => "#6ab8ff", LootRarity.Legendary => "#ff9a3a", _ => "#f2d27a"
    };

    /// <summary>A hero walking over loot takes it: stats for the rest of the match, a heal, or gold for the team.</summary>
    private void PickUpLoot(Unit hero)
    {
        foreach (var loot in units)
        {
            if (loot.Kind != "loot" || loot.Health <= 0 || Distance(hero, loot) >= 3) continue;
            var item = Catalog.LootItem(loot.Model);
            loot.Health = 0;
            if (item is null) continue;
            hero.Damage += item.Damage;
            hero.MaxHealth += item.Health;
            hero.Health = Math.Min(hero.MaxHealth, hero.Health + item.Health + hero.MaxHealth * item.Heal);
            hero.SpeedBonus = Math.Min(1, hero.SpeedBonus + item.Speed);
            Gold += item.Gold;
            if (!treasures.TryGetValue(hero.Owner, out var list)) treasures[hero.Owner] = list = [];
            list.Add(item.Id);
            if (list.Count > MaxTreasures) list.RemoveAt(0);
            Message = $"{item.Name} didapat! / {item.NameEn} found!";
        }
    }

    /// <summary>Which neutral units a fighting unit may pick as a target on its own.</summary>
    private static bool Engages(Unit unit, Unit neutral, double distance) => neutral.Kind switch
    {
        // Heroes always answer camps; other player units defend themselves at close range.
        "creep" => unit.Kind == "hero" || (unit.Owner >= 0 && distance <= 8),
        // Wildlife is fought only when it fights: a provoked animal, a boar that came close, or a thief.
        "wild" => neutral.ProvokedTicks > 0 || (distance <= 6 && Catalog.Wild(neutral.Model)?.Temperament is Temperament.Territorial or Temperament.Thief),
        "boss" => unit.Kind == "hero" || (unit.Hunter && !unit.Retreating && unit.Health > unit.MaxHealth * .6),
        _ => false
    };
}
