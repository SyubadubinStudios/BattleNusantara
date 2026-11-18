using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class DynamicNavigationTests
{
    [Fact]
    public void Build_OnActiveRoute_UnitReroutesWithoutEnteringFootprint()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.Move, 36, 12)));
        Assert.True(match.Execute(0, new(CommandKind.Build, 30, 18)));
        for (var tick = 0; tick < 45; tick++)
        {
            match.Advance();
            var hero = match.Snapshot().Units.Single(u => u.Id == 1);
            Assert.True(Math.Abs(hero.X - 30) > 5 || Math.Abs(hero.Z - 18) > 5);
        }
        var arrived = match.Snapshot().Units.Single(u => u.Id == 1);
        Assert.Equal(36, arrived.X, 5); Assert.Equal(12, arrived.Z, 5);
        Assert.False(match.Execute(0, new(CommandKind.Move, 30, 18)));
    }

    [Fact]
    public void Build_OnExistingDestination_UnitStopsOutsideNewBuilding()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.Move, 30, 18)));
        Assert.True(match.Execute(0, new(CommandKind.Build, 30, 18)));
        for (var tick = 0; tick < 40; tick++) match.Advance();
        var hero = match.Snapshot().Units.Single(u => u.Id == 1);
        Assert.True(Math.Abs(hero.X - 30) > 5 || Math.Abs(hero.Z - 18) > 5);
        Assert.True(Math.Abs(hero.X - 24) > .1 || Math.Abs(hero.Z - 24) > .1);
    }

    [Fact]
    public void Worker_DepositsFromOutsidePost_AndNeverWalksThroughIt()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.Build, 30, 32)));
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        var workerId = match.Snapshot().Units.Single(u => u.Kind == "worker").Id;
        var postId = match.Snapshot().Units.Single(u => u.Kind == "outpost").Id;
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [workerId], TargetId: postId)));
        for (var tick = 0; tick < 130; tick++) match.Advance();
        Assert.Equal(100, match.Snapshot().Units.Single(u => u.Id == postId).Construction);
        Assert.True(match.Execute(0, new(CommandKind.Gather, UnitIds: [workerId], TargetId: 1)));
        for (var tick = 0; tick < 100; tick++)
        {
            match.Advance();
            var worker = match.Snapshot().Units.Single(u => u.Id == workerId);
            Assert.True(Math.Abs(worker.X - 30) > 5 || Math.Abs(worker.Z - 32) > 5);
        }
        Assert.True(match.GoldCollected >= 40);
    }

    [Theory]
    [InlineData(34, 24)] // Second player's spawn, including exact clearance boundary.
    [InlineData(25, 13)] // Worker spawn clearance.
    public void Build_OnReservedSpawn_RejectsWithoutCharging(double x, double z)
    {
        var match = NewMatch();
        Assert.False(match.Execute(0, new(CommandKind.Build, x, z)));
        Assert.Equal(180, match.Gold);
    }

    [Fact]
    public void DestroyedOutpost_ReopensItsFootprint()
    {
        var match = NewMatch();
        // Outposts need explored ground: scout the site with the hero, then send it home.
        Assert.False(match.IsExplored(50, 50));
        Assert.False(match.Execute(0, new(CommandKind.Build, 50, 50)));
        Assert.True(match.Execute(0, new(CommandKind.Move, 34, 34)));
        while (!match.IsExplored(50, 50) && match.Tick < 2000) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.Build, 50, 50)));
        match.Execute(0, new(CommandKind.Move, 13, 13));
        Assert.False(match.Execute(0, new(CommandKind.Move, 50, 50)));
        while (match.Snapshot().Units.Any(u => u.Kind == "outpost") && match.Tick < 1200 && match.Status == MatchStatus.Playing)
            match.Advance();
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind == "outpost");
        Assert.True(match.Execute(0, new(CommandKind.Move, 50, 50)));
    }

    [Fact]
    public void SeparateMatches_DoNotShareDynamicObstacles()
    {
        var first = NewMatch(); var second = NewMatch();
        Assert.True(first.Execute(0, new(CommandKind.Build, 30, 18)));
        Assert.False(first.Execute(0, new(CommandKind.Move, 30, 18)));
        Assert.True(second.Execute(0, new(CommandKind.Move, 30, 18)));
    }

    private static Match NewMatch() => new(GameMode.Rts, "gajah-mada");

    [Fact]
    public void Lan_NewOutpostBlocksBothPlayersAndAppearsInBothSnapshots()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("host", "Rts", "gajah-mada", true);
        rooms.Enter("guest", host.Code, "republik", false);
        Assert.True(rooms.Command("host", new(CommandKind.Build, 30, 18)));
        Assert.False(rooms.Command("host", new(CommandKind.Move, 30, 18)));
        Assert.False(rooms.Command("guest", new(CommandKind.Move, 30, 18)));
        var snapshots = rooms.Advance();
        Assert.All(snapshots, s => Assert.Single(s.State.Units, u => u.Kind == "outpost"));
        Assert.Equal(snapshots[0].State.Units, snapshots[1].State.Units);
    }
}
