using System.Text.Json;
using System.Text.Json.Nodes;
using BattleNusantara.Core;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class HeroAbilityTests
{
    private static UnitView Hero(Match match) => match.Snapshot(fog: false).Units.Single(u => u.Kind == "hero" && u.Owner == 0);
    private static int Guardians(Match match) => match.Snapshot(fog: false).Units.Count(u => u.Kind == "guardian");

    [Fact]
    public void Catalog_SuperheroFaction_HasTwoOriginalHeroesWithTheirOwnUltimates()
    {
        var faction = Catalog.Heroes.Where(h => h.Faction == "Pahlawan Super").ToArray();
        Assert.Equal(["bledhek", "rara-angkasa"], faction.Select(h => h.Id));
        Assert.Equal([UltimateKind.Chain, UltimateKind.Leap], faction.Select(h => h.Ultimate));
        // The licensed comic characters named in the concept are not in the game.
        string[] licensed = ["gundala", "sri asih", "godam", "aquanus", "tira", "saras", "gaban", "si buta", "wiro", "jaka sembung"];
        foreach (var hero in Catalog.Heroes.Concat(Catalog.AllyChampions).Concat(Catalog.EnemyChampions))
            Assert.DoesNotContain(licensed, name => (hero.Id + " " + hero.Name + " " + hero.Description).Contains(name, StringComparison.OrdinalIgnoreCase));
        Assert.All(Catalog.Heroes.Take(4), hero => Assert.Equal(UltimateKind.Burst, hero.Ultimate));
    }

    [Fact]
    public void Ultimate_Chain_StrikesTheNearestEnemyThenGrantsSpeed()
    {
        var match = new Match(GameMode.Adventure, "bledhek");
        Assert.Equal(5, Guardians(match));
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        match.Advance();
        Assert.Equal(4, Guardians(match)); // The guardian at (35, 30) took the full strike.
        Assert.Equal("Sambaran Bledhek", match.Message);
        Assert.Equal(15.9, match.Snapshot().UltimateCooldown, 6); // One tick has passed.
        // Boosted for four seconds: 12 × 1.6 per second instead of 12.
        var start = Hero(match);
        Assert.True(match.Execute(0, new(CommandKind.Move, start.X, 90)));
        for (var i = 0; i < 10; i++) match.Advance();
        Assert.InRange(Hero(match).Z - start.Z, 17, 20);
        var stop = Hero(match);
        Assert.True(match.Execute(0, new(CommandKind.Move, stop.X, stop.Z)));
        for (var i = 0; i < 40; i++) match.Advance();
        var later = Hero(match);
        Assert.True(match.Execute(0, new(CommandKind.Move, later.X, 4)));
        match.Advance();
        Assert.InRange(later.Z - Hero(match).Z, 1.1, 1.3); // The boost has worn off.
    }

    [Fact]
    public void Ultimate_Chain_JumpsBetweenEnemiesThatStandClose()
    {
        // A saved Adventure with three sea spirits lined up eight apart east of the hero.
        var node = JsonNode.Parse(new Match(GameMode.Adventure, "bledhek", 1).Save()!)!.AsObject();
        node["SideStarted"] = true;
        var next = (int)node["NextId"]!;
        foreach (var x in new[] { 30, 38, 46 })
        {
            var spirit = JsonNode.Parse(node["Units"]!.AsArray().First(u => (string?)u!["Kind"] == "guardian")!.ToJsonString())!.AsObject();
            spirit["Id"] = ++next; spirit["Kind"] = "spirit";
            foreach (var key in new[] { "X", "TargetX", "OrderX", "RouteX" }) spirit[key] = x;
            foreach (var key in new[] { "Z", "TargetZ", "OrderZ", "RouteZ" }) spirit[key] = 20;
            foreach (var key in new[] { "Health", "MaxHealth" }) spirit[key] = 500;
            node["Units"]!.AsArray().Add(spirit);
        }
        node["NextId"] = next;
        var match = Match.Load(node.ToJsonString())!;
        Assert.NotNull(match);
        var before = match.Snapshot().Units.Where(u => u.Team == 1).ToDictionary(u => u.Id, u => u.Health);
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        var after = match.Snapshot().Units.Where(u => u.Team == 1).ToDictionary(u => u.Id, u => u.Health);
        var struck = before.Keys.Count(id => !after.TryGetValue(id, out var health) || health < before[id]);
        Assert.InRange(struck, 3, 6);
        // Each jump is weaker than the one before: the first spirit lost the most.
        var spirits = before.Keys.Where(id => id > next - 3).Order().ToArray();
        Assert.True(before[spirits[0]] - after[spirits[0]] > before[spirits[2]] - after.GetValueOrDefault(spirits[2], before[spirits[2]]));
    }

    [Fact]
    public void Ultimate_Leap_FliesToTheNearestEnemyAndBreaksIt()
    {
        var match = new Match(GameMode.Adventure, "rara-angkasa");
        var start = Hero(match);
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        var landed = Hero(match);
        var travelled = Math.Sqrt((landed.X - start.X) * (landed.X - start.X) + (landed.Z - start.Z) * (landed.Z - start.Z));
        Assert.InRange(travelled, 9, 12); // 12.5 to the guardian at (35, 30), stopping two short.
        match.Advance();
        Assert.Equal(4, Guardians(match));
        Assert.Equal("Hentakan Angkasa", match.Message);
    }

    [Fact]
    public void Ultimate_Leap_WithNoEnemyInReach_StaysAndStillCools()
    {
        var match = new Match(GameMode.Rts, "rara-angkasa");
        var start = Hero(match);
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        Assert.Equal((start.X, start.Z), (Hero(match).X, Hero(match).Z));
        Assert.False(match.Execute(0, new(CommandKind.Ultimate)));
    }

    [Fact]
    public void Snapshot_Units_CarryTheirModel()
    {
        var moba = new Match(GameMode.Moba, "bledhek").Snapshot(fog: false);
        Assert.Equal("bledhek", moba.Units.Single(u => u.Kind == "hero").Model);
        Assert.Equal(Catalog.AllyChampions.Select(c => c.Id).Concat(Catalog.EnemyChampions.Select(c => c.Id)).Order(), moba.Units.Where(u => u.Kind == "champion").Select(u => u.Model).Order());
        Assert.Equal("naga", moba.Units.Single(u => u.Kind == "boss").Model);
        var adventure = new Match(GameMode.Adventure, "gajah-mada").Snapshot();
        Assert.Equal(["captain", "keeper"], adventure.Units.Where(u => u.Kind == "npc").Select(u => u.Model).Order());
        Assert.Equal("kalatidha", adventure.Units.Single(u => u.Kind == "boss").Model);
        Assert.All(adventure.Units.Where(u => u.Kind is "guardian" or "relic"), u => Assert.Equal("", u.Model));
    }
}

public sealed class WeatherRuleTests
{
    [Fact]
    public void SightFactor_FollowsTheWeather()
    {
        Assert.Equal(1, Atmosphere.SightFactor(0));
        Assert.Equal(.8, Atmosphere.SightFactor(Atmosphere.WeatherTicks * 2));
        Assert.Equal(1, Atmosphere.SightFactor(Atmosphere.WeatherTicks * 4)); // Rain does not shorten sight.
        Assert.Equal(.75, Atmosphere.SightFactor(Atmosphere.WeatherTicks * 5));
    }

    [Fact]
    public void Vision_InMist_ShrinksTheTeamsSightButNotTheShrine()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        while (match.Tick < Atmosphere.WeatherTicks * 2 - 1) match.Advance();
        var clear = match.Snapshot().Vision;
        match.Advance();
        var mist = match.Snapshot().Vision;
        Assert.Equal("mist", Atmosphere.At(match.Tick).Weather);
        Assert.Equal(Match.ShrineVision, mist[0].Radius);
        Assert.Equal(Match.VisionRadius("hero") * .8, mist.Skip(1).Max(v => v.Radius), 6);
        Assert.Equal(Match.VisionRadius("hero"), clear.Skip(1).Max(v => v.Radius), 6);
    }
}

public sealed class SideQuestTests
{
    private static UnitView Hero(Match match) => match.Snapshot().Units.Single(u => u.Kind == "hero" && u.Owner == 0);
    private static QuestView Side(Match match) => match.Snapshot().Quests![^1];
    private static int SeedFor(string id) => Enumerable.Range(0, 100).First(seed => Story.PlanSideQuest(seed).Id == id);

    private static void Walk(Match match, double x, double z, int ticks = 400)
    {
        for (var i = 0; i < ticks; i++)
        {
            if (i % 10 == 0) match.Execute(0, new(CommandKind.Move, x, z));
            match.Advance();
            if (match.Snapshot().Dialogue.Length > 0) match.Execute(0, new(CommandKind.AdvanceDialogue));
            var hero = Hero(match);
            if (Math.Abs(hero.X - x) < 1 && Math.Abs(hero.Z - z) < 1) return;
        }
    }

    [Fact]
    public void PlanSideQuest_IsDeterministicVariedAndInsideTheArena()
    {
        Assert.Equal(Story.PlanSideQuest(42).Points, Story.PlanSideQuest(42).Points);
        var plans = Enumerable.Range(0, 64).Select(Story.PlanSideQuest).ToArray();
        Assert.Equal([Story.Spirits, Story.Fragments], plans.Select(p => p.Id).Distinct().Order());
        Assert.Equal([3, 4], plans.Select(p => p.Count).Distinct().Order());
        Assert.True(plans.Select(p => string.Join(';', p.Points)).Distinct().Count() > 20);
        foreach (var plan in plans)
        {
            Assert.Equal(plan.Count, plan.Points.Length);
            Assert.Equal(plan.Count, plan.Points.Distinct().Count());
            Assert.All(plan.Points, p => { Assert.InRange(p.X, 4, 96); Assert.InRange(p.Z, 4, 96); });
        }
    }

    [Fact]
    public void SideQuest_BeforeMeetingTheCaptain_IsOfferedButSpawnsNothing()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada", SeedFor(Story.Fragments));
        Assert.Equal(new QuestView(Story.Captain, QuestState.Active, 0, 1), Side(match));
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind is "flotsam" or "spirit");
    }

    [Fact]
    public void SideQuest_Fragments_CollectedForGoldAndHealth()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada", SeedFor(Story.Fragments));
        var plan = match.SidePlan;
        Walk(match, 12, 60);
        Assert.Equal(new QuestView(Story.Fragments, QuestState.Active, 0, plan.Count), Side(match));
        Assert.Equal(plan.Count, match.Snapshot().Units.Count(u => u.Kind == "flotsam"));
        foreach (var (x, z) in plan.Points) Walk(match, x, z);
        for (var i = 0; i < 5; i++) match.Advance();
        Assert.Equal(new QuestView(Story.Fragments, QuestState.Done, plan.Count, plan.Count), Side(match));
        Assert.Equal(Catalog.Hero("gajah-mada").Health + 40, Hero(match).MaxHealth);
        Assert.Contains("Side quest complete", match.Message);
    }

    [Fact]
    public void SideQuest_Spirits_AreFoughtAndDrivenOff()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada", SeedFor(Story.Spirits));
        var plan = match.SidePlan;
        Walk(match, 12, 60);
        Assert.Equal(plan.Count, match.Snapshot().Units.Count(u => u is { Kind: "spirit", Team: 1 }));
        foreach (var (x, z) in plan.Points) Walk(match, x, z, 300);
        for (var i = 0; i < 600 && Side(match).State != QuestState.Done; i++) match.Advance();
        Assert.Equal(QuestState.Done, Side(match).State);
        Assert.Equal(MatchStatus.Playing, match.Status);
    }

    [Fact]
    public void Save_DuringSideQuest_ResumesIdentically()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada", SeedFor(Story.Fragments));
        Walk(match, 12, 60);
        Walk(match, match.SidePlan.Points[0].X, match.SidePlan.Points[0].Z);
        var loaded = Match.Load(match.Save())!;
        Assert.NotNull(loaded);
        Assert.Equal(match.Seed, loaded.Seed);
        for (var i = 0; i < 300; i++)
        {
            foreach (var m in new[] { match, loaded }) { if (i % 10 == 0) m.Execute(0, new(CommandKind.Move, 40, 88)); m.Advance(); }
        }
        Assert.Equal(JsonSerializer.Serialize(match.Snapshot()), JsonSerializer.Serialize(loaded.Snapshot()));
    }

    [Fact]
    public void Load_VersionOneSave_GainsTheCaptainWithTheSideQuestNotStarted()
    {
        var node = JsonNode.Parse(new Match(GameMode.Adventure, "gajah-mada", 5).Save()!)!.AsObject();
        node["Version"] = 1; node.Remove("Seed"); node.Remove("SideStarted"); node.Remove("SideDone");
        node.Remove("Camps"); node.Remove("Wild"); node.Remove("Treasures");
        var units = node["Units"]!.AsArray();
        foreach (var unit in units.ToArray())
            if ((string?)unit!["Model"] == Story.CaptainNpc || (string?)unit["Kind"] is "creep" or "wild" or "loot") units.Remove(unit);
        foreach (var unit in units) unit!.AsObject().Remove("Model");
        var loaded = Match.Load(node.ToJsonString())!;
        Assert.NotNull(loaded);
        Assert.Equal(["captain", "keeper"], loaded.Snapshot().Units.Where(u => u.Kind == "npc").Select(u => u.Model).Order());
        Assert.Equal("kalatidha", loaded.Snapshot().Units.Single(u => u.Kind == "boss").Model);
        Assert.Equal(new QuestView(Story.Captain, QuestState.Active, 0, 1), loaded.Snapshot().Quests![^1]);
        Assert.Equal(1 + loaded.Snapshot().Units.Max(u => u.Id) - 1, loaded.Snapshot().Units.Max(u => u.Id));
        Assert.Equal(loaded.Snapshot().Units.Length, loaded.Snapshot().Units.Select(u => u.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("keeper", "Di mana keris pusaka itu?", "gua")]
    [InlineData("keeper", "how do I defeat the enemy", "kalatidha")]
    [InlineData("keeper", "what happened to your ship", null)]
    [InlineData("captain", "Kenapa badai terus datang?", "badai")]
    [InlineData("captain", "who are the sea spirits", "arwah")]
    [InlineData("captain", "", null)]
    public void MatchTopic_RoutesTypedQuestionsByKeyword(string npc, string text, string? topic) =>
        Assert.Equal(topic, Story.MatchTopic(npc, text)?.Id);
}
