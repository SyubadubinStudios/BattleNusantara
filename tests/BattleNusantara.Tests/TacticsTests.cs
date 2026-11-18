using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class TacticsTests
{
    [Fact]
    public void Path_AcrossRuins_GoesAroundWithClearanceAndDeterministicWaypoints()
    {
        var start = new MapPoint(20, 40); var goal = new MapPoint(75, 40);
        var map = NavigationMap.Rts;
        Assert.False(map.ClearSegment(start, goal));
        var route = map.FindPath(start, goal);
        Assert.True(route.Length > 1);
        Assert.Equal(goal, route[^1]);
        Assert.Equal(route, map.FindPath(start, goal));
        var previous = start;
        foreach (var point in route)
        {
            Assert.True(map.IsWalkable(point));
            Assert.True(map.ClearSegment(previous, point));
            previous = point;
        }
    }

    [Fact]
    public void Path_InvalidDestination_IsRejected()
    {
        Assert.Empty(NavigationMap.Rts.FindPath(new(20, 20), new(44, 40)));
        Assert.Empty(NavigationMap.Rts.FindPath(new(20, 20), new(double.NaN, 20)));
        Assert.Empty(NavigationMap.Rts.FindPath(new(20, 20), new(99, 20)));
        Assert.False(NavigationMap.Rts.ClearSegment(new(36, 40), new(52, 40), 0));
        Assert.True(NavigationMap.Rts.ClearSegment(new(20, 20), new(30, 20), 0));
    }

    [Fact]
    public void Hero_AcrossObstacle_NeverCrossesBlockedAreaAndReachesGoal()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Move, 20, 40)));
        for (var i = 0; i < 20; i++) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.Move, 70, 40)));
        var before = match.Snapshot().Units.Single(u => u.Kind == "hero");
        for (var i = 0; i < 100; i++)
        {
            match.Advance();
            var current = match.Snapshot().Units.Single(u => u.Kind == "hero");
            Assert.True(NavigationMap.Rts.IsWalkable(new(current.X, current.Z)));
            Assert.True(NavigationMap.Rts.ClearSegment(new(before.X, before.Z), new(current.X, current.Z)));
            before = current;
        }
        Assert.Equal(70, before.X, 5); Assert.Equal(40, before.Z, 5);
    }

    [Fact]
    public void SquadMove_UsesFormationWithoutMovingUnselectedHero()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        match.Execute(0, new(CommandKind.Recruit)); match.Execute(0, new(CommandKind.Recruit));
        var ids = Soldiers(match);
        Assert.True(match.Execute(0, new(CommandKind.Move, 30, 20, ids.Reverse().ToArray())));
        for (var i = 0; i < 40; i++) match.Advance();
        var soldiers = match.Snapshot().Units.Where(u => ids.Contains(u.Id)).OrderBy(u => u.Id).ToArray();
        Assert.Equal(28.5, soldiers[0].X, 5); Assert.Equal(31.5, soldiers[1].X, 5);
        Assert.All(soldiers, u => { Assert.Equal(20, u.Z, 5); Assert.Equal(UnitOrder.Move, u.Order); });
        var hero = match.Snapshot().Units.Single(u => u.Kind == "hero");
        Assert.Equal(24, hero.X); Assert.Equal(24, hero.Z);
    }

    [Fact]
    public void Hold_CancelsExistingMovementAndMaintainsPosition()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        match.Execute(0, new(CommandKind.Recruit));
        var ids = Soldiers(match);
        match.Execute(0, new(CommandKind.Move, 60, 20, ids));
        for (var i = 0; i < 10; i++) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.HoldPosition, UnitIds: ids)));
        var before = match.Snapshot().Units.Single(u => u.Id == ids[0]);
        for (var i = 0; i < 40; i++) match.Advance();
        var after = match.Snapshot().Units.Single(u => u.Id == ids[0]);
        Assert.Equal(before.X, after.X); Assert.Equal(before.Z, after.Z); Assert.Equal(UnitOrder.Hold, after.Order);
    }

    [Fact]
    public void AttackMove_EngagesThenResumesDestination()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        var hero = match.Snapshot().Units.Single(u => u.Kind == "hero");
        Assert.True(match.Execute(0, new(CommandKind.AttackMove, 76, 76, [hero.Id])));
        var engaged = false;
        for (var i = 0; i < 120; i++)
        {
            match.Advance();
            hero = match.Snapshot().Units.Single(u => u.Kind == "hero");
            if (match.Snapshot().Units.Any(u => u.Team == 1 && Math.Sqrt(Math.Pow(hero.X - u.X, 2) + Math.Pow(hero.Z - u.Z, 2)) <= 12.9))
            { engaged = true; break; }
        }
        Assert.True(engaged);
        var before = hero;
        match.Advance();
        hero = match.Snapshot().Units.Single(u => u.Id == hero.Id);
        Assert.Equal(before.X, hero.X); Assert.Equal(before.Z, hero.Z);
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        for (var i = 0; i < 5; i++) match.Advance();
        var after = match.Snapshot().Units.Single(u => u.Id == hero.Id);
        Assert.True(Math.Abs(after.X - hero.X) + Math.Abs(after.Z - hero.Z) > .1);
        Assert.Equal(UnitOrder.AttackMove, after.Order);
        Assert.Equal(76, after.TargetX); Assert.Equal(76, after.TargetZ);
    }

    [Fact]
    public void SquadOrder_InvalidSelectionOrBlockedGoal_DoesNotPartiallyApply()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        match.AddPlayer(1, "republik"); match.Execute(0, new(CommandKind.Recruit));
        var own = Soldiers(match)[0]; var foreign = match.Snapshot().Units.Single(u => u.Owner == 1).Id;
        Assert.False(match.Execute(0, new(CommandKind.Move, 40, 20, [own, foreign])));
        Assert.False(match.Execute(0, new(CommandKind.Move, 40, 20, [own, own])));
        Assert.False(match.Execute(0, new(CommandKind.Move, 44, 40, [own])));
        Assert.False(match.Execute(0, new(CommandKind.Move, 40, 20, [])));
        Assert.False(match.Execute(0, new(CommandKind.HoldPosition, UnitIds: Enumerable.Range(0, Match.MaxSelection + 1).ToArray())));
        Assert.Equal(UnitOrder.Auto, match.Snapshot().Units.Single(u => u.Id == own).Order);
    }

    [Theory]
    [InlineData(GameMode.Moba)]
    [InlineData(GameMode.Adventure)]
    public void SquadOrders_OutsideRts_AreRejected(GameMode mode)
    {
        var match = new Match(mode, "gajah-mada");
        Assert.False(match.Execute(0, new(CommandKind.AttackMove, 50, 50)));
        Assert.False(match.Execute(0, new(CommandKind.HoldPosition)));
        Assert.False(match.Execute(0, new(CommandKind.Move, 50, 50, [1])));
        Assert.Equal(mode == GameMode.Adventure ? 4 : 0, match.Snapshot().Obstacles.Length); // Adventure has the cave walls and its door.
    }

    [Fact]
    public void Lan_SquadOrdersAreBoundToConnectionOwner()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("host", "Rts", "gajah-mada", true);
        rooms.Enter("guest", host.Code, "republik", false);
        Assert.True(rooms.Command("host", new(CommandKind.Recruit)));
        var id = rooms.Advance().First().State.Units.Single(u => u.Owner == 0 && u.Kind == "soldier").Id;
        Assert.False(rooms.Command("guest", new(CommandKind.Move, 30, 20, [id])));
        Assert.True(rooms.Command("host", new(CommandKind.HoldPosition, UnitIds: [id])));
        Assert.All(rooms.Advance(), snapshot => Assert.Equal(UnitOrder.Hold, snapshot.State.Units.Single(u => u.Id == id).Order));
    }

    private static int[] Soldiers(Match match) => match.Snapshot().Units.Where(u => u.Owner == 0 && u.Kind == "soldier").Select(u => u.Id).ToArray();
}
