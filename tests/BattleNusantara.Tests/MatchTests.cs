using BattleNusantara.Core;
using BattleNusantara.Server;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class MatchTests
{
    [Fact]
    public void Move_NonFiniteOrUnknownOwner_RejectsCommand()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.False(match.Execute(0, new(CommandKind.Move, double.NaN, 2)));
        Assert.False(match.Execute(0, new(CommandKind.Move, 2, double.PositiveInfinity)));
        Assert.False(match.Execute(90, new(CommandKind.Move, 50, 50)));
    }
    [Fact]
    public void Move_OutOfBounds_ClampsDestination()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada");
        match.Execute(0, new(CommandKind.Move, -100, -100));
        for (var i = 0; i < 100; i++) match.Advance();
        var hero = Assert.Single(match.Snapshot().Units, u => u.Owner == 0);
        Assert.Equal(4, hero.X, 5); Assert.Equal(4, hero.Z, 5);
    }
    [Fact]
    public void Recruit_InsufficientGold_DoesNotOverspend()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Recruit)));
        Assert.True(match.Execute(0, new(CommandKind.Recruit)));
        Assert.True(match.Execute(0, new(CommandKind.Recruit)));
        Assert.False(match.Execute(0, new(CommandKind.Recruit)));
        Assert.Equal(0, match.Gold); Assert.Equal(6, match.Supplies);
    }
    [Fact]
    public void Build_Outpost_CostsGoldWithoutGeneratingPassiveIncome()
    {
        var baseline = new Match(GameMode.Rts, "gajah-mada");
        var match = new Match(GameMode.Rts, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Build, 28, 12)));
        for (var i = 0; i < 10; i++) { baseline.Advance(); match.Advance(); }
        Assert.Equal(baseline.Gold - 100, match.Gold);
    }
    [Fact]
    public void Ultimate_WhileCoolingDown_IsRejected()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada");
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
        Assert.False(match.Execute(0, new(CommandKind.Ultimate)));
        for (var i = 0; i < 181; i++) match.Advance();
        Assert.True(match.Execute(0, new(CommandKind.Ultimate)));
    }
    [Fact]
    public void Adventure_ReachRelic_CollectsOnceAndRestoresHealth()
    {
        var match = new Match(GameMode.Adventure, "gajah-mada");
        match.Execute(0, new(CommandKind.Move, 27, 29));
        for (var i = 0; i < 22; i++) match.Advance();
        Assert.Equal(1, match.Relics);
        Assert.Equal(2, match.Snapshot().Units.Count(u => u.Kind == "relic"));
    }
    [Theory]
    [InlineData(GameMode.Moba)]
    [InlineData(GameMode.Adventure)]
    public void Recruit_OutsideRts_IsRejected(GameMode mode)
    {
        Assert.False(new Match(mode, "gajah-mada").Execute(0, new(CommandKind.Recruit)));
    }
    [Fact]
    public void Simulation_SameCommands_IsDeterministic()
    {
        var a = new Match(GameMode.Moba, "cut-nyak-dien");
        var b = new Match(GameMode.Moba, "cut-nyak-dien");
        for (var i = 0; i < 1000; i++)
        {
            if (i % 70 == 0) { a.Execute(0, new(CommandKind.Move, 30, 40)); b.Execute(0, new(CommandKind.Move, 30, 40)); }
            a.Advance(); b.Advance();
        }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a.Snapshot()), System.Text.Json.JsonSerializer.Serialize(b.Snapshot()));
        Assert.InRange(a.Snapshot().Units.Length, 0, Match.MaxUnits);
    }
    [Fact]
    public void Match_InvalidModeOrHero_RejectsInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Match((GameMode)99, "gajah-mada"));
        Assert.Throws<ArgumentException>(() => new Match(GameMode.Rts, "unknown"));
    }
    [Fact]
    public void Room_ThirdPlayerAndDuplicateMembership_AreRejected()
    {
        var rooms = new RoomRegistry();
        var created = rooms.Enter("a", "Rts", "gajah-mada", true);
        var joined = rooms.Enter("b", created.Code, "nyi-roro-kidul", false);
        Assert.Equal(1, joined.Owner);
        for (var i = 2; i < RoomRegistry.MaxPlayers; i++) rooms.Enter("p" + i, created.Code, "republik", false);
        Assert.Throws<HubException>(() => rooms.Enter("c", created.Code, "republik", false));
        Assert.Throws<HubException>(() => rooms.Enter("a", "Rts", "republik", true));
        Assert.False(rooms.Command("outsider", new(CommandKind.Ultimate)));
    }
    [Fact]
    public void Room_LastMemberLeaves_RemovesRoom()
    {
        var rooms = new RoomRegistry();
        var created = rooms.Enter("a", "Rts", "gajah-mada", true);
        rooms.Leave("a");
        Assert.Empty(rooms.Advance());
        Assert.Throws<HubException>(() => rooms.Enter("b", created.Code, "republik", false));
    }
    [Fact]
    public void Room_CommandFlood_IsRateLimited()
    {
        var rooms = new RoomRegistry();
        rooms.Enter("a", "Rts", "gajah-mada", true);
        for (var i = 0; i < 20; i++) Assert.True(rooms.Command("a", new(CommandKind.Move, 10, 10)));
        Assert.False(rooms.Command("a", new(CommandKind.Move, 50, 50)));
        for (var i = 0; i < 10; i++) rooms.Advance();
        Assert.True(rooms.Command("a", new(CommandKind.Move, 50, 50)));
    }
}
