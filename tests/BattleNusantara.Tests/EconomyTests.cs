using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class EconomyTests
{
    [Fact]
    public void RecruitWorker_ChargesCostAndSupply_RejectsOverspending()
    {
        var match = NewMatch();
        for (var i = 0; i < 4; i++) Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        Assert.Equal(20, match.Gold); Assert.Equal(8, match.Supplies);
        Assert.False(match.Execute(0, new(CommandKind.RecruitWorker)));
        Assert.Equal(4, match.Snapshot().Units.Count(u => u.Kind == "worker"));
    }

    [Fact]
    public void Gather_CargoOnlyBecomesGoldAtDepot_AndIsConserved()
    {
        var match = NewMatch(); var id = Recruit(match);
        Assert.True(match.Execute(0, new(CommandKind.Gather, UnitIds: [id], TargetId: 1)));
        while (Worker(match, id).Cargo == 0 && match.Tick < 100) match.Advance();
        Assert.Equal(20, Worker(match, id).Cargo);
        Assert.Equal(0, match.GoldCollected);
        Assert.Equal(140 + match.Tick / 10 * 3, match.Gold);
        Assert.Equal(100, match.Snapshot().Resources[0].Remaining);
        while (match.GoldCollected == 0 && match.Tick < 200) match.Advance();
        Assert.Equal(20, match.GoldCollected);
        Assert.Equal(0, Worker(match, id).Cargo);
        Assert.Equal(140 + match.Tick / 10 * 3 + 20, match.Gold);
    }

    [Fact]
    public void Hold_StopsGatheringWithoutLosingCargo_ThenResumeDeposits()
    {
        var match = NewMatch(); var id = Recruit(match);
        match.Execute(0, new(CommandKind.Gather, UnitIds: [id], TargetId: 1));
        while (Worker(match, id).Cargo == 0 && match.Tick < 100) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.HoldPosition, UnitIds: [id])));
        var before = Worker(match, id);
        for (var i = 0; i < 30; i++) match.Advance();
        Assert.Equal(before.X, Worker(match, id).X); Assert.Equal(before.Z, Worker(match, id).Z);
        Assert.Equal(20, Worker(match, id).Cargo); Assert.Equal(0, match.GoldCollected);
        Assert.True(match.Execute(0, new(CommandKind.Gather, UnitIds: [id], TargetId: 2)));
        for (var i = 0; i < 60; i++) match.Advance();
        Assert.True(match.GoldCollected >= 20);
    }

    [Fact]
    public void MultipleWorkers_DepleteMineWithoutDuplicatingGold_ThenStop()
    {
        var match = NewMatch();
        match.Execute(0, new(CommandKind.Move, 20, 36));
        var ids = new[] { Recruit(match), Recruit(match), Recruit(match) };
        Assert.True(match.Execute(0, new(CommandKind.Gather, UnitIds: ids, TargetId: 1)));
        for (var i = 0; i < 230; i++) match.Advance();
        Assert.Equal(0, match.Snapshot().Resources[0].Remaining);
        Assert.Equal(120, match.GoldCollected);
        Assert.All(ids, id => { Assert.Equal(0, Worker(match, id).Cargo); Assert.Equal(UnitOrder.Hold, Worker(match, id).Order); });
        Assert.False(match.Execute(0, new(CommandKind.Gather, UnitIds: ids, TargetId: 1)));
    }

    [Fact]
    public void NearbyOutpost_ShortensDepositTrip()
    {
        var baseline = NewMatch(); var withPost = NewMatch();
        var a = Recruit(baseline); var b = Recruit(withPost);
        Assert.True(withPost.Execute(0, new(CommandKind.Build, 30, 32)));
        while (withPost.Snapshot().Units.Single(u => u.Kind == "outpost").Construction < 100 && withPost.Tick < 150)
        { baseline.Advance(); withPost.Advance(); }
        Assert.Equal(100, withPost.Snapshot().Units.Single(u => u.Kind == "outpost").Construction);
        baseline.Execute(0, new(CommandKind.Gather, UnitIds: [a], TargetId: 1));
        withPost.Execute(0, new(CommandKind.Gather, UnitIds: [b], TargetId: 1));
        var deadline = withPost.Tick + 100;
        while (withPost.GoldCollected == 0 && withPost.Tick < deadline) { baseline.Advance(); withPost.Advance(); }
        Assert.Equal(20, withPost.GoldCollected); Assert.Equal(0, baseline.GoldCollected);
    }

    [Theory]
    [InlineData(44, 40)] // Ruin
    [InlineData(20, 34)] // Mine
    [InlineData(24, 24)] // Hero
    [InlineData(13, 13)] // Shrine
    [InlineData(90, 90)] // Beyond build radius
    [InlineData(0, 0)]
    [InlineData(double.NaN, 20)]
    public void Build_InvalidPlacement_DoesNotChargeGold(double x, double z)
    {
        var match = NewMatch();
        Assert.False(match.Execute(0, new(CommandKind.Build, x, z)));
        Assert.Equal(180, match.Gold);
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Kind == "outpost");
    }

    [Fact]
    public void Build_OverlappingPost_IsRejected()
    {
        var match = NewMatch(); Assert.True(match.Execute(0, new(CommandKind.Build, 30, 32)));
        for (var i = 0; i < 70; i++) match.Advance();
        var gold = match.Gold;
        Assert.False(match.Execute(0, new(CommandKind.Build, 32, 33)));
        Assert.Equal(gold, match.Gold);
    }

    [Fact]
    public void Gather_ForeignWorkersMixedSelectionAndUnknownResource_AreRejected()
    {
        var match = NewMatch(); match.AddPlayer(1, "republik"); var id = Recruit(match);
        Assert.False(match.Execute(1, new(CommandKind.Gather, UnitIds: [id], TargetId: 1)));
        Assert.False(match.Execute(0, new(CommandKind.Gather, UnitIds: [1, id], TargetId: 1)));
        Assert.False(match.Execute(0, new(CommandKind.Gather, UnitIds: [id], TargetId: 99)));
        Assert.False(match.Execute(0, new(CommandKind.AttackMove, 30, 30, [id])));
        Assert.Equal(UnitOrder.Hold, Worker(match, id).Order);
    }

    [Theory]
    [InlineData(GameMode.Moba)]
    [InlineData(GameMode.Adventure)]
    public void Economy_OutsideRts_IsRejected(GameMode mode)
    {
        var match = new Match(mode, "gajah-mada");
        Assert.False(match.Execute(0, new(CommandKind.RecruitWorker)));
        Assert.False(match.Execute(0, new(CommandKind.Gather, UnitIds: [1], TargetId: 1)));
        Assert.False(match.Execute(0, new(CommandKind.Build, 30, 32)));
        Assert.Empty(match.Snapshot().Resources);
    }

    [Fact]
    public void Lan_WorkerOwnershipAndSharedResourceBalances_AreAuthoritative()
    {
        var rooms = new RoomRegistry(); var host = rooms.Enter("host", "Rts", "gajah-mada", true);
        rooms.Enter("guest", host.Code, "republik", false);
        Assert.True(rooms.Command("host", new(CommandKind.RecruitWorker)));
        var id = rooms.Advance()[0].State.Units.Single(u => u.Kind == "worker").Id;
        Assert.False(rooms.Command("guest", new(CommandKind.Gather, UnitIds: [id], TargetId: 1)));
        Assert.True(rooms.Command("host", new(CommandKind.Gather, UnitIds: [id], TargetId: 1)));
        for (var i = 0; i < 100; i++) rooms.Advance();
        var snapshots = rooms.Advance();
        Assert.Equal(snapshots[0].State.GoldCollected, snapshots[1].State.GoldCollected);
        Assert.True(snapshots[0].State.GoldCollected > 0);
        Assert.Equal(snapshots[0].State.Resources, snapshots[1].State.Resources);
    }

    private static Match NewMatch() => new(GameMode.Rts, "gajah-mada");

    [Fact]
    public void ReturnCargo_AfterHold_DepositsThenStaysIdle()
    {
        var match = NewMatch(); var id = Recruit(match);
        Assert.False(match.Execute(0, new(CommandKind.ReturnCargo, UnitIds: [id])));
        match.Execute(0, new(CommandKind.Gather, UnitIds: [id], TargetId: 1));
        while (Worker(match, id).Cargo == 0 && match.Tick < 100) match.Advance();
        match.Execute(0, new(CommandKind.HoldPosition, UnitIds: [id]));
        Assert.True(match.Execute(0, new(CommandKind.ReturnCargo, UnitIds: [id])));
        for (var i = 0; i < 100; i++) match.Advance();
        Assert.Equal(20, match.GoldCollected); Assert.Equal(UnitOrder.Hold, Worker(match, id).Order);
        Assert.Equal(100, match.Snapshot().Resources[0].Remaining);
    }
    private static int Recruit(Match match)
    {
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        return match.Snapshot().Units.Last(u => u.Kind == "worker").Id;
    }
    private static UnitView Worker(Match match, int id) => match.Snapshot().Units.Single(u => u.Id == id);
}
