using BattleNusantara.Core;
using BattleNusantara.Server;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class ConstructionQueueTests
{
    [Fact]
    public void Cancel_UnworkedFoundation_RefundsOnceAndReopensPath()
    {
        var match = NewMatch(); var post = Build(match, 30, 18);
        Assert.Equal(75, View(match, post).CancelRefund);
        Assert.True(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.Equal(155, match.Gold);
        Assert.False(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.Equal(155, match.Gold);
        Assert.True(match.Execute(0, new(CommandKind.Move, 30, 18)));
    }

    [Fact]
    public void Cancel_PartialFoundation_UsesActualWorkTicksAndStopsBuilder()
    {
        var match = NewMatch(); var worker = Recruit(match); var post = Build(match, 30, 18);
        while (View(match, post).Construction == 0 && match.Tick < 100) match.Advance();
        Advance(match, 39);
        Assert.Equal(50, View(match, post).Construction);
        Assert.Equal(37, View(match, post).CancelRefund);
        var gold = match.Gold;
        Assert.True(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.Equal(gold + 37, match.Gold);
        Assert.Equal(UnitOrder.Hold, View(match, worker).Order);
    }

    [Fact]
    public void Cancel_ForeignFinishedOrInvalidTarget_IsRejected()
    {
        var match = NewMatch(); match.AddPlayer(1, "republik");
        Recruit(match); var post = Build(match, 30, 18); var gold = match.Gold;
        Assert.False(match.Execute(1, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.False(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: 1)));
        Assert.False(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: 999)));
        Assert.Equal(gold, match.Gold);
        Advance(match, 120);
        Assert.Equal(100, View(match, post).Construction);
        gold = match.Gold;
        Assert.False(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.Equal(gold, match.Gold);
    }

    [Fact]
    public void Builder_CompletesTwoFoundationsWithoutAnotherOrder()
    {
        var match = FundedMatch(); var worker = Recruit(match);
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        while (View(match, first).Construction < 100 && match.Tick < 500) match.Advance();
        Assert.Equal(100, View(match, first).Construction);
        Assert.Equal(UnitOrder.Construct, View(match, worker).Order);
        Assert.Equal(12, View(match, worker).TargetZ);
        Advance(match, 130);
        Assert.Equal(100, View(match, second).Construction);
        Assert.Equal(UnitOrder.Hold, View(match, worker).Order);
    }

    [Fact]
    public void Hold_OptsOutOfQueue_UntilExplicitResume()
    {
        var match = FundedMatch(); var worker = Recruit(match);
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        Advance(match, 30);
        match.Execute(0, new(CommandKind.HoldPosition, UnitIds: [worker]));
        var progress = View(match, first).Construction;
        Advance(match, 30);
        Assert.Equal(progress, View(match, first).Construction);
        Assert.Equal(0, View(match, second).Construction);
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: first)));
        Advance(match, 230);
        Assert.Equal(100, View(match, first).Construction);
        Assert.Equal(100, View(match, second).Construction);
    }

    [Fact]
    public void Cancel_CurrentSite_RetargetsBuilderToWaitingSite()
    {
        var match = FundedMatch(); var worker = Recruit(match);
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        Assert.True(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: first)));
        Assert.Equal(UnitOrder.Construct, View(match, worker).Order);
        Assert.Equal(12, View(match, worker).TargetZ);
        Advance(match, 150);
        Assert.Equal(100, View(match, second).Construction);
    }

    [Fact]
    public void Queue_DoesNotTakeForeignFoundation()
    {
        var match = FundedMatch(); match.AddPlayer(1, "republik"); var worker = Recruit(match);
        Build(match, 30, 18);
        Assert.True(match.Execute(1, new(CommandKind.Build, 46, 12)));
        Advance(match, 150);
        Assert.Equal(UnitOrder.Hold, View(match, worker).Order);
        Assert.Equal(0, match.Snapshot().Units.Single(u => u.Kind == "outpost" && u.Owner == 1).Construction);
    }

    [Fact]
    public void Lan_CancellationSharesRefundAndRemovesSiteForBothPlayers()
    {
        var rooms = new RoomRegistry(); var host = rooms.Enter("host", "Rts", "gajah-mada", true);
        rooms.Enter("guest", host.Code, "republik", false);
        Assert.True(rooms.Command("host", new(CommandKind.Build, 30, 18)));
        var post = rooms.Advance()[0].State.Units.Single(u => u.Kind == "outpost").Id;
        Assert.False(rooms.Command("guest", new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.True(rooms.Command("host", new(CommandKind.CancelConstruction, TargetId: post)));
        var states = rooms.Advance();
        Assert.All(states, s => { Assert.Equal(155, s.State.Gold); Assert.DoesNotContain(s.State.Units, u => u.Id == post); });
    }

    private static Match NewMatch() => new(GameMode.Rts, "gajah-mada");

    [Fact]
    public void Queue_SkipsSiteAlreadyAssignedToAnotherWorker()
    {
        var match = FundedMatch(); Advance(match, 40);
        var firstWorker = Recruit(match); var secondWorker = Recruit(match);
        var first = Build(match, 30, 18); var second = Build(match, 46, 12);
        while (View(match, first).Construction < 100 && match.Tick < 500) match.Advance();
        Assert.Equal(100, View(match, first).Construction);
        Assert.Equal(UnitOrder.Hold, View(match, firstWorker).Order);
        Assert.Equal(UnitOrder.Construct, View(match, secondWorker).Order);
        Assert.InRange(View(match, second).Construction, 0, 99);
        Advance(match, 150);
        Assert.Equal(100, View(match, second).Construction);
    }

    [Fact]
    public void Cancel_BuilderWithCargo_DoesNotConvertOrLoseCargo()
    {
        var match = NewMatch(); var worker = Recruit(match); var post = Build(match, 30, 18);
        match.Execute(0, new(CommandKind.Gather, UnitIds: [worker], TargetId: 1));
        while (View(match, worker).Cargo == 0 && match.Tick < 100) match.Advance();
        Assert.Equal(20, View(match, worker).Cargo);
        match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: post));
        var gold = match.Gold;
        Assert.True(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: post)));
        Assert.Equal(gold + 75, match.Gold);
        Assert.Equal(20, View(match, worker).Cargo);
        Assert.Equal(0, match.GoldCollected);
    }

    [Fact]
    public void DestroyedCurrentSite_BuilderContinuesToSurvivingFoundation()
    {
        var match = FundedMatch();
        // Outposts need explored ground: scout the site with the hero, then send it home.
        Assert.False(match.IsExplored(50, 50));
        Assert.False(match.Execute(0, new(CommandKind.Build, 50, 50)));
        Assert.True(match.Execute(0, new(CommandKind.Move, 34, 34)));
        while (!match.IsExplored(50, 50) && match.Tick < 2000) match.Advance();
        var exposed = Build(match, 50, 50); var next = Build(match, 30, 18);
        Assert.True(match.Execute(0, new(CommandKind.Move, 24, 24)));
        while (View(match, exposed).Health == 44 && match.Tick < 1200) match.Advance();
        Assert.InRange(View(match, exposed).Health, 1, 43);
        var worker = Recruit(match);
        Assert.True(match.Execute(0, new(CommandKind.Construct, UnitIds: [worker], TargetId: exposed)));
        Advance(match, 270);
        Assert.DoesNotContain(match.Snapshot().Units, u => u.Id == exposed);
        Assert.Equal(100, View(match, next).Construction);
        Assert.False(match.Execute(0, new(CommandKind.CancelConstruction, TargetId: exposed)));
    }
    private static Match FundedMatch()
    {
        var match = NewMatch(); Advance(match, 300); return match;
    }
    private static UnitView View(Match match, int id) => match.Snapshot().Units.Single(u => u.Id == id);
    private static void Advance(Match match, int ticks) { for (var i = 0; i < ticks; i++) match.Advance(); }
    private static int Build(Match match, double x, double z)
    {
        Assert.True(match.Execute(0, new(CommandKind.Build, x, z)));
        return match.Snapshot().Units.Last(u => u.Kind == "outpost").Id;
    }
    private static int Recruit(Match match)
    {
        Assert.True(match.Execute(0, new(CommandKind.RecruitWorker)));
        return match.Snapshot().Units.Last(u => u.Kind == "worker").Id;
    }
}
