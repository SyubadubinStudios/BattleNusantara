using System.Diagnostics;
using BattleNusantara.Core;
using BattleNusantara.Server.Platform;
using BattleNusantara.UI;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace BattleNusantara.Tests;

/// <summary>
/// Real SignalR clients against the real server pipeline on loopback, in one process. The default size is small so the
/// suite stays fast; set NUSANTARA_LOAD_ROOMS (up to 32) and NUSANTARA_LOAD_SECONDS for the full run in docs/PERFORMANCE.md.
/// Client and server share this machine's CPU, so the numbers are a floor for a single host, not a capacity claim.
/// </summary>
public sealed class LoadTests(ITestOutputHelper output)
{
    private static int Setting(string name, int fallback, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? Math.Clamp(value, 1, max) : fallback;

    [Fact]
    public async Task Server_FullRoomsOfTenPlayers_KeepsTheTickAndDeliversSnapshots()
    {
        var roomCount = Setting("NUSANTARA_LOAD_ROOMS", 4, Server.RoomRegistry.MaxRooms);
        var seconds = Setting("NUSANTARA_LOAD_SECONDS", 5, 600);
        var (app, url, database) = await PlatformHostTests.Start();
        var clients = new List<HubConnection>();
        try
        {
            var received = 0L; var bytes = 0L; var accepted = 0L; var sent = 0L;
            var heroes = new List<(HubConnection Hub, int Hero, int Seat)>();
            for (var room = 0; room < roomCount; room++)
            {
                string? code = null;
                for (var seat = 0; seat < Match.MaxPlayers; seat++)
                {
                    var hub = await PlatformHostTests.Hub(url, null);
                    clients.Add(hub);
                    hub.On<MatchView>("State", view => { Interlocked.Increment(ref received); if (Interlocked.Read(ref received) % 97 == 0) Interlocked.Add(ref bytes, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(view).Length * 97L); });
                    var joined = code is null ? await hub.InvokeAsync<RoomJoined>("Create", "Rts", "gajah-mada", (string?)null) : await hub.InvokeAsync<RoomJoined>("Join", code, Catalog.Heroes[seat % Catalog.Heroes.Count].Id);
                    code = joined.Code;
                    heroes.Add((hub, joined.State.Units.First(u => u.Kind == "hero" && u.Owner == joined.Owner).Id, seat));
                }
            }
            var metrics = app.Services.GetRequiredService<LoopMetrics>();
            metrics.Reset(); Interlocked.Exchange(ref received, 0); Interlocked.Exchange(ref bytes, 0);
            var watch = Stopwatch.StartNew();
            // Every player gives two orders a second, as an active player would.
            var round = 0;
            while (watch.Elapsed.TotalSeconds < seconds)
            {
                var orders = heroes.Select(async h =>
                {
                    Interlocked.Increment(ref sent);
                    if (await h.Hub.InvokeAsync<bool>("Command", new GameCommand(CommandKind.Move, 20 + (h.Seat * 3 + round) % 30, 46 + round % 8, [h.Hero]))) Interlocked.Increment(ref accepted);
                });
                await Task.WhenAll(orders);
                round++;
                await Task.Delay(500);
            }
            watch.Stop();
            var ticks = metrics.Ticks; var expected = ticks * clients.Count;
            var delivered = expected == 0 ? 0 : Interlocked.Read(ref received) / (double)expected;
            output.WriteLine($"LOAD rooms={roomCount} clients={clients.Count} seconds={watch.Elapsed.TotalSeconds:0.0} ticks={ticks} overruns={metrics.Overruns} tick mean={metrics.MeanMs:0.00} ms worst={metrics.WorstMs:0.00} ms snapshots={Interlocked.Read(ref received)} delivered={delivered:P1} traffic~{Interlocked.Read(ref bytes) / 1024.0 / watch.Elapsed.TotalSeconds:0} KB/s commands={sent} accepted={accepted}");
            Assert.InRange(ticks, (long)(seconds * 10 * .8), (long)(watch.Elapsed.TotalSeconds * 10 + 2)); // The 10 Hz loop kept its pace.
            Assert.True(metrics.Overruns <= Math.Max(1, ticks / 20), $"{metrics.Overruns} of {ticks} ticks overran 100 ms.");
            Assert.True(delivered > .9, $"Only {delivered:P1} of snapshots arrived.");
            Assert.True(accepted > sent * .75, $"Only {accepted} of {sent} commands were accepted.");
            Assert.Equal((roomCount, clients.Count, 0), app.Services.GetRequiredService<Server.RoomRegistry>().Census());
        }
        finally
        {
            await Task.WhenAll(clients.Select(c => c.DisposeAsync().AsTask()));
            await app.DisposeAsync(); database.Dispose();
        }
    }
}
