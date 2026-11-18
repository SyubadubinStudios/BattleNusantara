using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class FogOfWarTests
{
    private static double Distance(double x, double z, double a, double b) => Math.Sqrt((x - a) * (x - a) + (z - b) * (z - b));
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }

    [Fact]
    public void Snapshot_RtsStart_HidesDistantEnemiesButKeepsOwnUnitsAndVision()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        var hidden = match.Snapshot();
        var full = match.Snapshot(fog: false);
        Assert.Contains(full.Units, u => u.Team == 1);
        Assert.DoesNotContain(hidden.Units, u => u.Team == 1);
        Assert.Contains(hidden.Units, u => u.Kind == "hero" && u.Owner == 0);
        Assert.Contains(hidden.Vision, v => v is { X: 13, Z: 13, Radius: Match.ShrineVision });
        Assert.Contains(hidden.Vision, v => v is { X: 24, Z: 24, Radius: 26 });
        Assert.Equal(full.Vision, hidden.Vision);
    }

    [Fact]
    public void Snapshot_EveryTick_ShowsExactlyTheEnemiesInsideVision()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Recruit)));
        var seen = false;
        for (var tick = 0; tick < 400 && match.Status == MatchStatus.Playing; tick++)
        {
            match.Advance();
            var view = match.Snapshot(); var full = match.Snapshot(fog: false);
            var expected = full.Units.Where(u => u.Team != 1 || full.Vision.Any(v => Distance(u.X, u.Z, v.X, v.Z) <= v.Radius
                && NavigationMap.Rts.ClearSegment(new(v.X, v.Z), new(u.X, u.Z), 0))).Select(u => u.Id);
            Assert.Equal(expected, view.Units.Select(u => u.Id));
            seen |= view.Units.Any(u => u.Team == 1);
        }
        Assert.True(seen, "Enemy waves should eventually walk into vision.");
    }

    [Fact]
    public void Snapshot_HeroScoutsForward_RevealsEnemiesNearHero()
    {
        var match = new Match(GameMode.Rts, "republik");
        Assert.True(match.Execute(0, new(CommandKind.Move, 58, 76)));
        Advance(match, 60);
        var view = match.Snapshot();
        var hero = view.Units.Single(u => u.Kind == "hero");
        Assert.Contains(view.Units, u => u.Team == 1 && Distance(u.X, u.Z, hero.X, hero.Z) <= 26);
    }

    [Fact]
    public void Snapshot_SecondPlayer_SharesTeamVision()
    {
        var match = new Match(GameMode.Rts, "gajah-mada"); match.AddPlayer(1, "republik");
        Assert.True(match.Execute(1, new(CommandKind.Move, 58, 76)));
        Advance(match, 60);
        var first = match.Snapshot(0); var second = match.Snapshot(1);
        Assert.Contains(first.Units, u => u.Team == 1);
        Assert.Equal(first.Units.Select(u => u.Id), second.Units.Select(u => u.Id));
        Assert.Equal(first.Vision, second.Vision);
    }

    [Fact]
    public void Vision_Outpost_GrowsWhenConstructionCompletes()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        Assert.True(match.Execute(0, new(CommandKind.Build, 30, 18)));
        Assert.Contains(match.Snapshot().Vision, v => v is { X: 30, Z: 18, Radius: 8 });
        Advance(match, 130);
        Assert.Equal(100, match.Snapshot().Units.Single(u => u.Kind == "outpost").Construction);
        Assert.Contains(match.Snapshot().Vision, v => v is { X: 30, Z: 18, Radius: 20 });
    }

    [Fact]
    public void Snapshot_EnemyBehindRuin_IsHiddenUntilLineOfSightIsClear()
    {
        // A watcher between the ruins cannot see the approach behind the ruin at (62,60).
        Assert.False(NavigationMap.Rts.ClearSegment(new(30, 40), new(54, 40), 0));
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Move, 52, 48)));
        var hiddenBehindRuin = false;
        for (var tick = 0; tick < 900 && match.Status == MatchStatus.Playing; tick++)
        {
            match.Advance();
            var view = match.Snapshot(); var full = match.Snapshot(fog: false);
            foreach (var enemy in full.Units.Where(u => u.Team == 1))
            {
                var inRadius = full.Vision.Where(v => Distance(enemy.X, enemy.Z, v.X, v.Z) <= v.Radius).ToArray();
                var clear = inRadius.Any(v => NavigationMap.Rts.ClearSegment(new(v.X, v.Z), new(enemy.X, enemy.Z), 0));
                Assert.Equal(clear, view.Units.Any(u => u.Id == enemy.Id));
                hiddenBehindRuin |= inRadius.Length > 0 && !clear;
            }
        }
        Assert.True(hiddenBehindRuin, "An enemy should have been inside a vision radius but behind a ruin at least once.");
    }

    [Fact]
    public void Placement_UnexploredGround_IsRejectedUntilScouted()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.IsExplored(30, 18));
        Assert.True(match.CanPlaceOutpost(30, 18));
        Assert.False(match.IsExplored(60, 30));
        Assert.False(match.CanPlaceOutpost(60, 30));
        var gold = match.Gold;
        Assert.False(match.Execute(0, new(CommandKind.Build, 60, 30)));
        Assert.Equal(gold, match.Gold);
        Assert.True(match.Execute(0, new(CommandKind.Move, 46, 28)));
        while (!match.IsExplored(60, 30) && match.Tick < 300) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.Move, 24, 24)));
        for (var i = 0; i < 60; i++) match.Advance();
        // Explored ground stays buildable after the scout leaves.
        Assert.True(match.IsExplored(60, 30));
        Assert.True(new Match(GameMode.Adventure, "gajah-mada").IsExplored(90, 90));
        Assert.False(match.IsExplored(double.NaN, 5));
        Assert.False(match.IsExplored(120, 5));
    }

    [Theory]
    [InlineData(GameMode.Moba)]
    [InlineData(GameMode.Adventure)]
    public void Snapshot_OtherModes_HaveNoFog(GameMode mode)
    {
        var match = new Match(mode, "gajah-mada");
        var view = match.Snapshot();
        Assert.Empty(view.Vision);
        Assert.Contains(view.Units, u => u.Team == 1);
        Assert.Equal(match.Snapshot(fog: false).Units.Length, view.Units.Length);
    }
}

public sealed class ConstructionPriorityTests
{
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }
    private static Match Funded() { var match = new Match(GameMode.Rts, "gajah-mada"); Advance(match, 700); Assert.Equal(MatchStatus.Playing, match.Status); return match; }
    private static UnitView View(Match match, int id) => match.Snapshot().Units.Single(u => u.Id == id);
    private static int Build(Match match, double x, double z, int owner = 0)
    {
        Assert.True(match.Execute(owner, new(CommandKind.Build, x, z)));
        return match.Snapshot().Units.Last(u => u.Kind == "outpost").Id;
    }
    private static int Recruit(Match match)
    {
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        return match.Snapshot().Units.Last(u => u.Kind == "worker").Id;
    }

    [Fact]
    public void Queue_NewFoundations_AreNumberedInPlacementOrder()
    {
        var match = Funded();
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        Assert.Equal(1, View(match, first).Queue);
        Assert.Equal(2, View(match, second).Queue);
        Assert.All(match.Snapshot().Units.Where(u => u.Kind != "outpost"), u => Assert.Equal(0, u.Queue));
    }

    [Fact]
    public void Prioritize_LaterFoundation_MovesItFirstAndBuilderTakesItNext()
    {
        var match = Funded();
        var first = Build(match, 30, 18); var second = Build(match, 46, 12); var third = Build(match, 40, 24);
        Assert.True(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: third)));
        Assert.Equal([2, 3, 1], new[] { first, second, third }.Select(id => View(match, id).Queue));
        Assert.True(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: second)));
        Assert.Equal([3, 1, 2], new[] { first, second, third }.Select(id => View(match, id).Queue));
        var worker = Recruit(match);
        Assert.True(match.Execute(0, new(CommandKind.HoldPosition, UnitIds: [worker])));
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: first)));
        while (View(match, first).Construction < 100 && match.Tick < 1300) match.Advance();
        Assert.Equal(100, View(match, first).Construction);
        Assert.Equal(0, View(match, first).Queue);
        Assert.Equal(UnitOrder.Construct, View(match, worker).Order);
        Assert.Equal((46d, 12d), (View(match, worker).TargetX, View(match, worker).TargetZ));
        Assert.Equal([1, 2], new[] { second, third }.Select(id => View(match, id).Queue));
    }

    [Fact]
    public void Prioritize_FirstFinishedForeignOrUnknownTarget_IsRejected()
    {
        var match = Funded(); match.AddPlayer(1, "republik");
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: first)));
        Assert.False(match.Execute(1, new(CommandKind.PrioritizeConstruction, TargetId: second)));
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: 1)));
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: 999)));
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction)));
        Assert.Equal([1, 2], new[] { first, second }.Select(id => View(match, id).Queue));
        var worker = Recruit(match);
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: second)));
        while (View(match, second).Construction < 100 && match.Tick < 1300) match.Advance();
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: second)));
    }

    [Fact]
    public void Prioritize_OtherModes_IsRejected()
    {
        var match = new Match(GameMode.Moba, "gajah-mada");
        Assert.False(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: 1)));
    }

    [Fact]
    public void Queue_TwoPlayers_IsNumberedPerOwner()
    {
        var match = Funded(); match.AddPlayer(1, "republik");
        var mine = Build(match, 30, 18); var theirs = Build(match, 46, 12, owner: 1); var mineLater = Build(match, 40, 24);
        Assert.Equal([1, 1, 2], new[] { mine, theirs, mineLater }.Select(id => View(match, id).Queue));
        Assert.True(match.Execute(0, new(CommandKind.PrioritizeConstruction, TargetId: mineLater)));
        Assert.Equal([2, 1, 1], new[] { mine, theirs, mineLater }.Select(id => View(match, id).Queue));
    }

    [Fact]
    public void Cancel_FrontFoundation_RenumbersRemainingQueue()
    {
        var match = Funded();
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        Assert.True(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: first)));
        Assert.Equal(1, View(match, second).Queue);
    }
}
