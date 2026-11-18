using BattleNusantara.Core;
using BattleNusantara.Server;
using BattleNusantara.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BattleNusantara.Tests;

public sealed class LanLifecycleTests
{
    private static void Advance(RoomRegistry rooms, int ticks) { for (var i = 0; i < ticks; i++) rooms.Advance(); }
    private static MatchView? StateFor(RoomRegistry rooms, string connection) => rooms.Advance().Where(s => s.Connection == connection).Select(s => s.State).FirstOrDefault();

    [Fact]
    public void Rejoin_WithTokenInsideGrace_RestoresTheSameSeat()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        var guest = rooms.Enter("b", host.Code, "republik", false);
        Assert.NotEqual(host.Token, guest.Token);
        Assert.Equal(32, guest.Token.Length);
        // Wait beside the shrine, where heroes heal, so the idle hero survives the wait.
        Assert.True(rooms.Command("b", new(CommandKind.Move, 14, 20)));
        Advance(rooms, 30);
        rooms.Disconnect("b");
        Assert.False(rooms.Command("b", new(CommandKind.Ultimate)));
        Advance(rooms, RoomRegistry.GraceTicks - 2); // StateFor advances once more.
        Assert.Contains(StateFor(rooms, "a")!.Units, u => u.Kind == "hero" && u.Owner == 1);
        var back = rooms.Rejoin("b2", host.Code, guest.Token);
        Assert.Equal(1, back.Owner);
        Assert.Equal(guest.Token, back.Token);
        Assert.False(back.Spectator);
        Assert.True(rooms.Command("b2", new(CommandKind.Move, 30, 30)));
        // The seat no longer expires once the player is back.
        Advance(rooms, RoomRegistry.GraceTicks + 5);
        Assert.Contains(StateFor(rooms, "b2")!.Units, u => u.Kind == "hero" && u.Owner == 1);
    }

    [Fact]
    public void Rejoin_WrongTokenOrRoom_IsRejected()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        rooms.Disconnect("a");
        Assert.Throws<HubException>(() => rooms.Rejoin("x", host.Code, ""));
        Assert.Throws<HubException>(() => rooms.Rejoin("x", host.Code, new string('0', 32)));
        Assert.Throws<HubException>(() => rooms.Rejoin("x", "ZZZZZZ", host.Token));
        Assert.Equal(0, rooms.Rejoin("x", host.Code, host.Token).Owner);
        Assert.Throws<HubException>(() => rooms.Rejoin("x", host.Code, host.Token));
    }

    [Fact]
    public void Rejoin_WhileOldConnectionStillMapped_TakesOverTheSeat()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("old", "Rts", "gajah-mada", true);
        Assert.Equal(0, rooms.Rejoin("new", host.Code, host.Token).Owner);
        Assert.False(rooms.Command("old", new(CommandKind.Move, 30, 30)));
        Assert.True(rooms.Command("new", new(CommandKind.Move, 30, 30)));
        // The stale connection closing later must not disturb the new one.
        rooms.Disconnect("old");
        Assert.True(rooms.Command("new", new(CommandKind.Move, 32, 30)));
        Assert.Single(rooms.Advance());
    }

    [Fact]
    public void Disconnect_PastGrace_RemovesHeroHandsOverUnitsAndFreesTheSeat()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        var guest = rooms.Enter("b", host.Code, "republik", false);
        rooms.Disconnect("b");
        Advance(rooms, RoomRegistry.GraceTicks);
        var view = StateFor(rooms, "a")!;
        Assert.DoesNotContain(view.Units, u => u.Owner == 1);
        Assert.Single(view.Units, u => u.Kind == "hero");
        Assert.Throws<HubException>(() => rooms.Rejoin("b2", host.Code, guest.Token));
        var replacement = rooms.Enter("c", host.Code, "cut-nyak-dien", false);
        Assert.Equal(1, replacement.Owner);
        Assert.NotEqual(guest.Token, replacement.Token);
    }

    [Fact]
    public void RemovePlayer_WithTroops_HandsThemToTheRemainingPlayer()
    {
        var match = new Match(GameMode.Rts, "gajah-mada"); match.AddPlayer(1, "republik");
        Assert.True(match.Execute(1, new(CommandKind.Recruit)));
        Assert.True(match.Execute(1, new(CommandKind.RecruitWorker)));
        match.RemovePlayer(1);
        var view = match.Snapshot();
        Assert.False(match.HasPlayer(1));
        Assert.DoesNotContain(view.Units, u => u.Owner == 1);
        Assert.Single(view.Units, u => u.Kind == "hero");
        Assert.Contains(view.Units, u => u.Kind == "soldier" && u.Owner == 0);
        Assert.Contains(view.Units, u => u.Kind == "worker" && u.Owner == 0);
        Assert.False(match.Execute(1, new(CommandKind.Recruit)));
        match.RemovePlayer(1); // Removing twice is harmless.
        match.AddPlayer(1, "cut-nyak-dien");
        Assert.True(match.Execute(1, new(CommandKind.Move, 30, 30)));
    }

    [Fact]
    public void Leave_Explicit_FreesSeatImmediatelyAndRoomContinues()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        rooms.Enter("b", host.Code, "republik", false);
        rooms.Leave("a");
        Assert.Equal(1, rooms.RoomCount);
        var view = StateFor(rooms, "b")!;
        Assert.Single(view.Units, u => u.Kind == "hero");
        Assert.Equal(1, view.Units.Single(u => u.Kind == "hero").Owner);
        Assert.Throws<HubException>(() => rooms.Rejoin("a2", host.Code, host.Token));
        Assert.Equal(0, rooms.Enter("c", host.Code, "gajah-mada", false).Owner);
        rooms.Leave("b"); rooms.Leave("c");
        Assert.Equal(0, rooms.RoomCount);
    }

    [Fact]
    public void Room_EveryoneDisconnected_ClosesAfterGrace()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Moba", "gajah-mada", true);
        rooms.Disconnect("a");
        Advance(rooms, RoomRegistry.GraceTicks - 1);
        Assert.Equal(1, rooms.RoomCount);
        rooms.Advance();
        Assert.Equal(0, rooms.RoomCount);
        Assert.Throws<HubException>(() => rooms.Rejoin("a2", host.Code, host.Token));
    }

    [Fact]
    public void Leave_MobaTeammate_ReturnsAnAiAlly()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Moba", "gajah-mada", true);
        rooms.Enter("b", host.Code, "republik", false);
        Assert.Equal(3, StateFor(rooms, "a")!.Units.Count(u => u.Kind == "champion" && u.Team == 0));
        rooms.Leave("b");
        var view = StateFor(rooms, "a")!;
        Assert.Equal(4, view.Units.Count(u => u.Kind == "champion" && u.Team == 0));
        Assert.Single(view.Units, u => u.Kind == "hero");
    }

    [Fact]
    public void Spectator_Watches_ReceivesStateButCannotCommand()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        var watcher = rooms.Watch("w", host.Code);
        Assert.Equal((-1, true, ""), (watcher.Owner, watcher.Spectator, watcher.Token));
        Assert.False(rooms.Command("w", new(CommandKind.Recruit)));
        Assert.False(rooms.Command("w", new(CommandKind.Move, 30, 30)));
        var states = rooms.Advance();
        Assert.Equal(2, states.Count);
        Assert.Equal(states.Single(s => s.Connection == "a").State.Units.Select(u => u.Id), states.Single(s => s.Connection == "w").State.Units.Select(u => u.Id));
        Assert.Throws<HubException>(() => rooms.Watch("w", host.Code));
        Assert.Throws<HubException>(() => rooms.Watch("x", "ZZZZZZ"));
        rooms.Disconnect("w");
        Assert.Equal(1, rooms.RoomCount);
        Assert.Single(rooms.Advance());
        // Spectators do not count as players: the second seat is still free.
        Assert.Equal(1, rooms.Enter("b", host.Code, "republik", false).Owner);
    }

    [Fact]
    public void Spectator_SeatsAreLimitedAndRoomClosingReleasesThem()
    {
        var rooms = new RoomRegistry();
        var host = rooms.Enter("a", "Rts", "gajah-mada", true);
        for (var i = 0; i < RoomRegistry.MaxSpectators; i++) rooms.Watch($"w{i}", host.Code);
        Assert.Throws<HubException>(() => rooms.Watch("extra", host.Code));
        rooms.Leave("a");
        Assert.Equal(0, rooms.RoomCount);
        // Former spectators are free to enter another room.
        var next = rooms.Enter("w0", "Rts", "gajah-mada", true);
        Assert.Equal(0, next.Owner);
    }

    private static async Task<WebApplication> StartServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<RoomRegistry>();
        builder.Services.AddHostedService<BattleLoop>();
        var app = builder.Build();
        app.MapHub<BattleHub>("/hubs/battle");
        await app.StartAsync();
        return app;
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 400; i++) { if (condition()) return; await Task.Delay(50); }
        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Client_DroppedConnection_ReconnectsToTheSameSeatAndKeepsPlaying()
    {
        GameClient.ReconnectDelay = TimeSpan.FromMilliseconds(100);
        await using var app = await StartServer();
        var rooms = app.Services.GetRequiredService<RoomRegistry>();
        await using var client = new GameClient();
        await client.Connect(app.Urls.First(), null, GameMode.Rts, "gajah-mada");
        var code = client.RoomCode;
        await Until(() => client.View!.Tick > 3, "first snapshots");
        Assert.True(await client.Command(new(CommandKind.Recruit)));

        await client.DropConnectionForTest();
        await Until(() => !client.Reconnecting && client.NetworkError is null && client.View is not null, "reconnect");
        var resumed = client.View!.Tick;
        await Until(() => client.View!.Tick > resumed + 3, "snapshots after reconnect");
        Assert.Equal(code, client.RoomCode);
        Assert.Equal(0, client.Owner);
        Assert.Null(client.NetworkError);
        Assert.Contains(client.View!.Units, u => u.Kind == "soldier" && u.Owner == 0);
        Assert.True(await client.Command(new(CommandKind.Move, 30, 30)));
        Assert.Equal(1, rooms.RoomCount);

        await client.Leave();
        await Until(() => rooms.RoomCount == 0, "room to close after leaving");
        Assert.Null(client.View);
    }

    [Fact]
    public async Task Client_Spectator_SeesTheMatchAndCannotCommand()
    {
        await using var app = await StartServer();
        await using var host = new GameClient();
        await using var watcher = new GameClient();
        await host.Connect(app.Urls.First(), null, GameMode.Adventure, "gajah-mada");
        await watcher.Connect(app.Urls.First(), host.RoomCode!.ToLowerInvariant(), GameMode.Rts, "republik", watch: true);
        Assert.True(watcher.Spectating);
        Assert.Equal(-1, watcher.Owner);
        Assert.Equal(host.RoomCode, watcher.RoomCode);
        await Until(() => watcher.View!.Tick > 3, "spectator snapshots");
        Assert.Equal(GameMode.Adventure, watcher.View!.Mode);
        Assert.NotNull(watcher.View.Quests);
        Assert.False(await watcher.Command(new(CommandKind.Move, 30, 30)));
        await Assert.ThrowsAsync<ArgumentException>(() => watcher.Connect(app.Urls.First(), " ", GameMode.Rts, "republik", watch: true));
    }

    [Fact]
    public async Task Client_ServerGone_ReportsDisconnectInsteadOfRetryingForever()
    {
        GameClient.ReconnectDelay = TimeSpan.FromMilliseconds(100);
        GameClient.ReconnectAttempts = 2;
        try
        {
        await using var app = await StartServer();
        var rooms = app.Services.GetRequiredService<RoomRegistry>();
        await using var client = new GameClient();
        await client.Connect(app.Urls.First(), null, GameMode.Rts, "gajah-mada");
        await Until(() => client.View!.Tick > 3, "first snapshots");
        Assert.Equal(1, rooms.RoomCount);
        // The server goes away for good: after its attempts the client reports the loss.
        await app.StopAsync();
        await Until(() => client.NetworkError is not null, "disconnect to be reported");
        Assert.False(client.Reconnecting);
        Assert.False(await client.Command(new(CommandKind.Move, 30, 30)));
        }
        finally { GameClient.ReconnectAttempts = 10; }
    }
}
