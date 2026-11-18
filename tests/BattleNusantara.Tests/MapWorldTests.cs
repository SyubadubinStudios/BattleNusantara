using BattleNusantara.Core;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class MapTests
{
    private static readonly HashSet<string> Biomes = ["subur", "hutan", "tandus", "gurun", "sabana", "rawa", "pantai", "vulkanik"];
    private static readonly HashSet<string> Edges = ["gunung", "vulkan", "laut", "hutan", "gurun"];

    public static TheoryData<string> AllMaps => new(Maps.All.Select(m => m.Id));

    [Fact]
    public void Maps_EveryMode_HasMapsAndTheyComeInThreeSizes()
    {
        Assert.Equal(Maps.All.Count, Maps.All.Select(m => m.Id).Distinct().Count());
        foreach (var mode in Enum.GetValues<GameMode>()) Assert.NotEmpty(Maps.For(mode));
        Assert.Equal([80d, 100d, 140d], Maps.For(GameMode.Rts).Select(m => m.Size).Order());
        Assert.Equal([80d, 100d, 140d], Maps.For(GameMode.Moba).Select(m => m.Size).Order());
        Assert.Equal(["besar", "kecil", "sedang"], Maps.All.Select(m => m.SizeName).Distinct().Order());
        // The defaults are the maps the game always had, at the same coordinates.
        Assert.Equal(Maps.LembahCandi, Maps.Default(GameMode.Rts).Id);
        Assert.Equal(NavigationMap.Rts.Obstacles.Select(o => (o.X, o.Z, o.Width, o.Depth)), Maps.Default(GameMode.Rts).Obstacles.Select(o => (o.X, o.Z, o.Width, o.Depth)));
        var layout = new MapLayout(100);
        Assert.Equal((13d, 87d), (layout.AllyBase, layout.EnemyBase));
        Assert.Equal((24d, 24d), layout.HeroSpawn(0));
        Assert.Equal([(20d, 48d), (36d, 36d), (48d, 20d)], layout.AllyTowers);
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Map_Landscape_UsesKnownBiomesAndEdges(string id)
    {
        var map = Maps.Find(id)!;
        Assert.Equal(4, map.Edges.Length);
        Assert.All(map.Edges, edge => Assert.Contains(edge, Edges));
        Assert.NotEmpty(map.Regions);
        Assert.All(map.Regions, region => { Assert.Contains(region.Biome, Biomes); Assert.InRange(region.Radius, 4, map.Size); });
        Assert.All(map.Obstacles, o => Assert.Contains(o.Kind, (string[])["ruin", "rock", "water", "grove", "wall", "door"]));
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Map_EveryPlaceThatMatters_IsWalkableAndReachable(string id)
    {
        var map = Maps.Find(id)!;
        var match = new Match(map.Mode, "gajah-mada", 3, id);
        var layout = match.Layout;
        var navigation = map.Mode == GameMode.Adventure ? NavigationMap.AdventureOpen : new NavigationMap(map.Obstacles, map.Size);
        var start = new MapPoint(layout.HeroSpawn(0).X, layout.HeroSpawn(0).Z);
        Assert.True(navigation.IsWalkable(start), $"{id}: hero spawn");
        void Reachable(double x, double z, string what)
        {
            Assert.True(navigation.IsWalkable(new(x, z)), $"{id}: {what} at ({x}, {z}) is not walkable");
            Assert.NotEmpty(navigation.FindPath(start, new(x, z)));
        }
        foreach (var resource in map.Resources) Reachable(resource.X, resource.Z, $"mine {resource.Id}");
        foreach (var camp in map.Camps) Reachable(camp.X, camp.Z, $"camp {camp.Id}");
        if (map.Mode == GameMode.Moba)
        {
            for (var lane = 0; lane < 3; lane++) Reachable(layout.Lane(lane).X, layout.Lane(lane).Z, $"lane {lane}");
            foreach (var (x, z) in layout.AllyTowers) { Reachable(x, z, "ally tower"); Reachable(map.Size - x, map.Size - z, "enemy tower"); }
            Reachable(layout.Boss.X, layout.Boss.Z, "boss");
        }
        if (map.Mode != GameMode.Adventure) Reachable(layout.EnemyBase - 6, layout.EnemyBase - 6, "enemy base");
        Assert.Equal(id, match.Snapshot().Map);
        Assert.Equal(map.Size, match.Snapshot().Size);
    }

    [Theory]
    [MemberData(nameof(AllMaps))]
    public void Map_ThreeMinutesOfPlay_KeepsEveryUnitOnTheArena(string id)
    {
        var map = Maps.Find(id)!;
        var match = new Match(map.Mode, "gajah-mada", 11, id);
        for (var tick = 0; tick < 1800 && match.Status == MatchStatus.Playing; tick++)
        {
            if (tick % 50 == 0) match.Execute(0, new(CommandKind.Move, map.Size / 2, map.Size / 2));
            match.Advance();
        }
        Assert.All(match.Snapshot(fog: false).Units, u => { Assert.InRange(u.X, 0, map.Size); Assert.InRange(u.Z, 0, map.Size); });
        Assert.InRange(match.Snapshot(fog: false).Units.Length, 1, Match.MaxUnits);
    }

    [Fact]
    public void Match_UnknownOrForeignMap_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Match(GameMode.Rts, "gajah-mada", 0, "nowhere"));
        Assert.Throws<ArgumentException>(() => new Match(GameMode.Rts, "gajah-mada", 0, Maps.ArenaJagat));
        Assert.Equal(Maps.HutanLarangan, new Match(GameMode.Rts, "gajah-mada", 0, Maps.HutanLarangan).Map.Id);
        var rooms = new Server.RoomRegistry();
        Assert.Throws<Microsoft.AspNetCore.SignalR.HubException>(() => rooms.Enter("c1", "Rts", "gajah-mada", true, null, Maps.ArenaJagat));
        Assert.Equal(Maps.PesisirSelatan, rooms.Enter("c2", "Rts", "gajah-mada", true, null, Maps.PesisirSelatan).State.Map);
        Assert.Equal(Maps.LembahCandi, rooms.Enter("c3", "Rts", "gajah-mada", true, null, null).State.Map);
    }
}

public sealed class NeutralTests
{
    private static UnitView Hero(Match match) => match.Snapshot(fog: false).Units.Single(u => u.Kind == "hero" && u.Owner == 0);
    private static UnitView[] Camp(Match match, double x, double z) => match.Snapshot(fog: false).Units.Where(u => u.Kind == "creep" && Math.Abs(u.X - x) < 6 && Math.Abs(u.Z - z) < 6).ToArray();

    [Fact]
    public void Camps_SpawnTheirTierAndIgnoreTheArmies()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.Equal(["tuyul", "tuyul", "tuyul"], Camp(match, 18, 76).Select(u => u.Model));
        Assert.Equal(["genderuwo", "siluman-buaya", "siluman-buaya"], Camp(match, 90, 30).Select(u => u.Model).Order());
        Assert.All(match.Snapshot(fog: false).Units.Where(u => u.Kind == "creep"), u => Assert.Equal(3, u.Team));
        // Waves march past for two minutes; nobody fights the camps, so they stay whole.
        for (var i = 0; i < 1200; i++) match.Advance();
        Assert.All(match.Snapshot(fog: false).Units.Where(u => u.Kind == "creep"), u => Assert.Equal(u.MaxHealth, u.Health));
    }

    [Fact]
    public void Camp_ClearedByTheHero_PaysBountyDropsLootAndReturnsLater()
    {
        var match = new Match(GameMode.Rts, "gajah-mada", 5);
        var gold = match.Snapshot().Gold;
        for (var i = 0; i < 1500 && Camp(match, 18, 76).Length > 0; i++)
        {
            if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, 18, 70));
            match.Advance();
        }
        Assert.Empty(Camp(match, 18, 76));
        Assert.Equal(MatchStatus.Playing, match.Status);
        Assert.Contains("Kamp lemah ditaklukkan", match.Message);
        var expected = Match.CampDrops(5, 1, 1, CampTier.Lemah);
        Assert.Equal(expected, match.Snapshot().Units.Where(u => u.Kind == "loot").Select(u => u.Model));
        Assert.True(match.Snapshot().Gold >= gold + 60);
        // Pick up whatever dropped.
        var before = Hero(match);
        foreach (var loot in match.Snapshot().Units.Where(u => u.Kind == "loot").ToArray())
            for (var i = 0; i < 100 && match.Snapshot().Units.Any(u => u.Id == loot.Id); i++) { match.Execute(0, new(CommandKind.Move, loot.X, loot.Z)); match.Advance(); }
        Assert.Equal(expected, match.Snapshot().Treasures);
        Assert.Equal(expected.Sum(id => Catalog.LootItem(id)!.Health) + before.MaxHealth, Hero(match).MaxHealth);
        // The camp returns once its time is up and nobody stands in it.
        match.Execute(0, new(CommandKind.Move, 24, 24));
        for (var i = 0; i < Catalog.CampRespawnTicks(CampTier.Lemah) + 100; i++) match.Advance();
        Assert.Equal(3, Camp(match, 18, 76).Length);
    }

    [Fact]
    public void Camp_WipedOutInOneBlow_ClearsOnceAndDropsOnce()
    {
        // A seed where a second or third clear would also drop something, so a repeated clear could not hide.
        var seed = Enumerable.Range(1, 200).First(s => Match.CampDrops(s, 1, 2, CampTier.Lemah).Length + Match.CampDrops(s, 1, 3, CampTier.Lemah).Length > 0);
        var match = new Match(GameMode.Rts, "gajah-mada", seed);
        // Walk up to the weak camp, stopping outside its reach, then fell all three tuyul with one ultimate.
        for (var i = 0; i < 200 && Math.Abs(Hero(match).Z - 64) > .5; i++) { if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, 18, 64)); match.Advance(); }
        Assert.Equal(3, Camp(match, 18, 76).Length);
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        match.Advance();
        Assert.Empty(Camp(match, 18, 76));
        Assert.Equal(Match.CampDrops(seed, 1, 1, CampTier.Lemah), match.Snapshot().Units.Where(u => u.Kind == "loot").Select(u => u.Model));
        // It returns once, after one respawn time.
        match.Execute(0, new(CommandKind.Move, 24, 24));
        for (var i = 0; i < Catalog.CampRespawnTicks(CampTier.Lemah) + 100; i++) match.Advance();
        Assert.Equal(3, Camp(match, 18, 76).Length);
    }

    [Fact]
    public void Creeps_ChasingTooFar_GoHomeAndHeal()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        for (var i = 0; i < 300 && Camp(match, 90, 30).All(u => u.Health == u.MaxHealth); i++)
        {
            if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, 84, 30));
            match.Advance();
        }
        Assert.Contains(Camp(match, 90, 30), u => u.Health < u.MaxHealth);
        match.Execute(0, new(CommandKind.Move, 24, 24));
        for (var i = 0; i < 400; i++) match.Advance();
        Assert.All(Camp(match, 90, 30), u => Assert.Equal(u.MaxHealth, u.Health));
        Assert.Equal(3, Camp(match, 90, 30).Length);
    }

    [Theory]
    [InlineData(CampTier.Lemah)]
    [InlineData(CampTier.Sedang)]
    [InlineData(CampTier.Kuat)]
    [InlineData(CampTier.Purba)]
    public void CampDrops_AreDeterministicAndMatchTheTier(CampTier tier)
    {
        for (var seed = 0; seed < 40; seed++)
        {
            var drops = Match.CampDrops(seed, 2, 1, tier);
            Assert.Equal(drops, Match.CampDrops(seed, 2, 1, tier));
            var rarities = drops.Select(d => Catalog.LootItem(d)!.Rarity).ToArray();
            switch (tier)
            {
                case CampTier.Lemah: Assert.InRange(rarities.Length, 0, 1); Assert.All(rarities, r => Assert.Equal(LootRarity.Minor, r)); break;
                case CampTier.Sedang: Assert.Equal(LootRarity.Minor, rarities[0]); Assert.InRange(rarities.Length, 1, 2); break;
                case CampTier.Kuat: Assert.Equal(LootRarity.Major, rarities[0]); break;
                default: Assert.Equal([LootRarity.Legendary, LootRarity.Major, LootRarity.Treasure], rarities); break;
            }
        }
        // Clearing the same camp again rolls again.
        Assert.Contains(Enumerable.Range(1, 30), clears => !Match.CampDrops(7, 2, clears, CampTier.Sedang).SequenceEqual(Match.CampDrops(7, 2, 1, CampTier.Sedang)));
    }

    [Fact]
    public void Wildlife_PlacedFromTheSeed_InTheirBiomes()
    {
        var match = new Match(GameMode.Rts, "gajah-mada", 9);
        var animals = match.Snapshot(fog: false).Units.Where(u => u.Kind == "wild").ToArray();
        Assert.NotEmpty(animals);
        Assert.All(animals, a => Assert.NotNull(Catalog.Wild(a.Model)));
        Assert.Equal(animals.Select(a => (a.Model, a.X, a.Z)), new Match(GameMode.Rts, "gajah-mada", 9).Snapshot(fog: false).Units.Where(u => u.Kind == "wild").Select(a => (a.Model, a.X, a.Z)));
        // Thieves only appear where there are workers to rob.
        Assert.DoesNotContain(new Match(GameMode.Moba, "gajah-mada", 9).Snapshot().Units, u => u.Model == "kera-pencuri");
    }

    [Fact]
    public void Kancil_RunsFromAnApproachingHero()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        var id = match.PlaceWildlife("kancil", 40, 30);
        double Gap() { var hero = Hero(match); var deer = match.Snapshot(fog: false).Units.Single(u => u.Id == id); return Math.Sqrt((hero.X - deer.X) * (hero.X - deer.X) + (hero.Z - deer.Z) * (hero.Z - deer.Z)); }
        for (var i = 0; i < 120; i++) { if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, 40, 30)); match.Advance(); }
        Assert.True(Gap() > 4, $"The kancil should keep away, gap {Gap():0.0}.");
        Assert.Equal(40, match.Snapshot(fog: false).Units.Single(u => u.Id == id).Health);
    }

    [Fact]
    public void Boar_ChargesAnyoneWhoComesClose_EvenEnemySoldiers()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        var boar = match.PlaceWildlife("babi-hutan", 74, 74);
        // The first enemy wave forms up at (76, 76): right on top of the boar.
        for (var i = 0; i < 80; i++) match.Advance();
        var enemies = match.Snapshot(fog: false).Units.Where(u => u.Kind == "soldier" && u.Team == 1).ToArray();
        Assert.True(enemies.Any(e => e.Health < e.MaxHealth) || match.Snapshot(fog: false).Units.All(u => u.Id != boar),
            "The boar should have gone for the enemy soldiers, or been killed by them.");
    }

    [Fact]
    public void Thief_StealsAWorkersLoad_AndKillingItReturnsTheGold()
    {
        var match = new Match(GameMode.Rts, "gajah-mada", 1);
        foreach (var animal in match.Snapshot(fog: false).Units.Where(u => u.Kind == "wild").ToArray()) { }
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        var worker = match.Snapshot().Units.Single(u => u.Kind == "worker").Id;
        match.LoadCargo(worker, 20);
        var w = match.Snapshot().Units.Single(u => u.Id == worker);
        var thief = match.PlaceWildlife("kera-pencuri", w.X + 6, w.Z + 6);
        for (var i = 0; i < 100 && match.Snapshot().Units.Single(u => u.Id == worker).Cargo > 0; i++) match.Advance();
        Assert.Equal(0, match.Snapshot().Units.Single(u => u.Id == worker).Cargo);
        Assert.Equal(20, match.Snapshot(fog: false).Units.Single(u => u.Id == thief).Cargo);
        Assert.Contains("Kera pencuri", match.Message);
        // The hero runs it down; the gold comes back with the bounty.
        var gold = match.Snapshot().Gold;
        for (var i = 0; i < 600 && match.Snapshot(fog: false).Units.Any(u => u.Id == thief); i++)
        {
            var t = match.Snapshot(fog: false).Units.SingleOrDefault(u => u.Id == thief);
            if (t is not null && i % 5 == 0) match.Execute(0, new(CommandKind.Move, t.X, t.Z));
            match.Advance();
        }
        Assert.DoesNotContain(match.Snapshot(fog: false).Units, u => u.Id == thief);
        Assert.True(match.Snapshot().Gold >= gold + 20 + Catalog.Wild("kera-pencuri")!.Gold, "The stolen gold and the bounty should be paid.");
    }

    [Fact]
    public void Neutrals_InRtsFog_AreHiddenUntilSeen()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind == "creep" && u.X > 80);
        Assert.Contains(match.Snapshot(fog: false).Units, u => u.Kind == "creep" && u.X > 80);
    }

    [Fact]
    public void Save_WithCampsLootAndTreasures_ResumesIdentically()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada", 4);
        for (var i = 0; i < 1500 && Camp(match, 50, 8).Length > 0; i++)
        {
            if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, 50, 12));
            if (match.Snapshot().Dialogue.Length > 0) match.Execute(0, new(CommandKind.AdvanceDialogue));
            match.Advance();
        }
        Assert.Empty(Camp(match, 50, 8));
        var json = match.Save()!;
        var loaded = Match.Load(json);
        Assert.NotNull(loaded);
        Assert.Equal(json, loaded!.Save());
        for (var i = 0; i < 400; i++) { foreach (var m in new[] { match, loaded }) { if (i % 10 == 0) m.Execute(0, new(CommandKind.Move, 50, 9)); m.Advance(); } }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(match.Snapshot()), System.Text.Json.JsonSerializer.Serialize(loaded.Snapshot()));
        Assert.True(json.Length <= 12000, $"Save is {json.Length} characters.");
    }
}
