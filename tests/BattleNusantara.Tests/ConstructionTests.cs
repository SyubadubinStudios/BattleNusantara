using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class ConstructionTests
{
    [Fact]
    public void Foundation_WithoutWorker_RemainsIncompleteAndBlocksWalking()
    {
        var match = NewMatch();
        Assert.True(match.Execute(0, new(CommandKind.Build, 30, 18)));
        Advance(match, 100);
        Assert.Equal(0, Post(match).Construction);
        Assert.Equal(44, Post(match).Health);
        Assert.Equal(110, match.Gold);
        Assert.False(match.Execute(0, new(CommandKind.Move, 30, 18)));
    }

    [Fact]
    public void IdleWorker_IsAssigned_RequiresEightyWorkTicks_ThenStops()
    {
        var match = NewMatch(); var worker = Recruit(match);
        match.Execute(0, new(CommandKind.Build, 30, 18));
        Assert.Equal(UnitOrder.Construct, Unit(match, worker).Order);
        while (Post(match).Construction == 0 && match.Tick < 100) match.Advance();
        Assert.Equal(1, Post(match).Construction);
        Advance(match, 78);
        Assert.Equal(98, Post(match).Construction);
        match.Advance();
        Assert.Equal(100, Post(match).Construction);
        Assert.Equal(220, Post(match).Health, 6);
        Assert.Equal(UnitOrder.Hold, Unit(match, worker).Order);
        Assert.False(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: Post(match).Id)));
    }

    [Fact]
    public void Hold_PausesConstruction_AnotherWorkerResumesWithoutExtraCharge()
    {
        var match = NewMatch(); var first = Recruit(match); var second = Recruit(match);
        match.Execute(0, new(CommandKind.Build, 30, 18));
        Advance(match, 30);
        Assert.True(match.Execute(0, new(CommandKind.HoldPosition, UnitIds: [first])));
        var progress = Post(match).Construction; var health = Post(match).Health;
        Advance(match, 20);
        Assert.Equal(progress, Post(match).Construction); Assert.Equal(health, Post(match).Health);
        var gold = match.Gold;
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [second], TargetId: Post(match).Id)));
        Assert.Equal(gold, match.Gold);
        Advance(match, 110);
        Assert.Equal(100, Post(match).Construction);
    }

    [Fact]
    public void MultipleBuilders_DoNotMultiplyConstructionSpeed()
    {
        var match = NewMatch(); var a = Recruit(match); var b = Recruit(match);
        match.Execute(0, new(CommandKind.Build, 30, 18));
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [a, b], TargetId: Post(match).Id)));
        while (Post(match).Construction == 0 && match.Tick < 100) match.Advance();
        Advance(match, 39);
        Assert.Equal(50, Post(match).Construction);
    }

    [Fact]
    public void IncompleteOutpost_DoesNotAcceptCargo()
    {
        var match = NewMatch(); var worker = Recruit(match);
        match.Execute(0, new(CommandKind.Gather, UnitIds: [worker], TargetId: 1));
        match.Execute(0, new(CommandKind.Build, 30, 32));
        Assert.Equal(UnitOrder.Gather, Unit(match, worker).Order);
        while (match.GoldCollected == 0 && match.Tick < 150) match.Advance();
        Assert.Equal(20, match.GoldCollected);
        Assert.Equal(0, Post(match).Construction);
        var unit = Unit(match, worker);
        Assert.InRange(Math.Sqrt(Math.Pow(unit.X - 13, 2) + Math.Pow(unit.Z - 13, 2)), 0, 3);
    }

    [Fact]
    public void Construct_InvalidTargetsAndForeignOrMixedWorkers_RejectAtomically()
    {
        var match = NewMatch(); match.AddPlayer(1, "republik"); var worker = Recruit(match);
        match.Execute(1, new(CommandKind.Build, 30, 18));
        Assert.False(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: Post(match).Id)));
        Assert.False(match.Execute(1, new(CommandKind.Construct, UnitIds: [worker], TargetId: Post(match).Id)));
        Assert.False(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: 999)));
        Assert.Equal(UnitOrder.Hold, Unit(match, worker).Order);
        var own = NewMatch(); var ownWorker = Recruit(own); own.Execute(0, new(CommandKind.Build, 30, 18));
        own.Execute(0, new(CommandKind.HoldPosition, UnitIds: [ownWorker]));
        Assert.False(own.Execute(0, new(CommandKind.Construct, UnitIds: [1, ownWorker], TargetId: Post(own).Id)));
        Assert.Equal(UnitOrder.Hold, Unit(own, ownWorker).Order);
    }

    [Fact]
    public void Lan_ConstructionProgressIsSharedAndOwnershipEnforced()
    {
        var rooms = new RoomRegistry(); var host = rooms.Enter("host", "Rts", "gajah-mada", true);
        rooms.Enter("guest", host.Code, "republik", false);
        rooms.Command("host", new(CommandKind.RecruitWorker));
        rooms.Command("host", new(CommandKind.Build, 30, 18));
        var initial = rooms.Advance()[0].State;
        var worker = initial.Units.Single(u => u.Kind == "worker").Id;
        var post = initial.Units.Single(u => u.Kind == "outpost").Id;
        Assert.False(rooms.Command("guest", new(CommandKind.Construct, UnitIds: [worker], TargetId: post)));
        for (var i = 0; i < 120; i++) rooms.Advance();
        var snapshots = rooms.Advance();
        Assert.All(snapshots, s => Assert.Equal(100, s.State.Units.Single(u => u.Id == post).Construction));
    }

    private static Match NewMatch() => new(GameMode.Rts, "gajah-mada");

    [Fact]
    public void Construct_CarriedGoldIsPreservedUntilExplicitReturn()
    {
        var match = NewMatch(); var worker = Recruit(match);
        match.Execute(0, new(CommandKind.Build, 30, 18));
        match.Execute(0, new(CommandKind.Gather, UnitIds: [worker], TargetId: 1));
        while (Unit(match, worker).Cargo == 0 && match.Tick < 100) match.Advance();
        Assert.Equal(20, Unit(match, worker).Cargo);
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: Post(match).Id)));
        Advance(match, 150);
        Assert.Equal(100, Post(match).Construction);
        Assert.Equal(20, Unit(match, worker).Cargo);
        Assert.Equal(0, match.GoldCollected);
        Assert.True(match.Execute(0, new(CommandKind.ReturnCargo, UnitIds: [worker])));
        Advance(match, 50);
        Assert.Equal(20, match.GoldCollected);
        Assert.Equal(0, Unit(match, worker).Cargo);
    }
    private static UnitView Post(Match match) => match.Snapshot().Units.Single(u => u.Kind == "outpost");
    private static UnitView Unit(Match match, int id) => match.Snapshot().Units.Single(u => u.Id == id);
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }
    private static int Recruit(Match match)
    {
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        return match.Snapshot().Units.Last(u => u.Kind == "worker").Id;
    }
}
