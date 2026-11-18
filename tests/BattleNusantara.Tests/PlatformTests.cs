using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using BattleNusantara.Core;
using BattleNusantara.Server;
using BattleNusantara.Server.Platform;
using BattleNusantara.UI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BattleNusantara.Tests;

internal static class TestSetup
{
    // Production uses 600,000 PBKDF2 iterations; tests only need the format and the comparison to be right.
    [ModuleInitializer] internal static void FastHashes() => PasswordHasher.Iterations = 1000;
}

internal sealed class Clock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>A throwaway SQLite database file, deleted with the fixture.</summary>
internal sealed class TempDatabase : IDisposable
{
    public string File { get; } = Path.Combine(Path.GetTempPath(), $"nusantara-test-{Guid.NewGuid():N}.db");
    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = File }.ToString();
    public async Task<SqlAccountStore> Store()
    {
        var text = ConnectionString;
        var store = new SqlAccountStore(() => new SqliteConnection(text), sqlite: true);
        await store.Migrate();
        return store;
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { File, File + "-wal", File + "-shm" }) try { System.IO.File.Delete(path); } catch (IOException) { }
    }
}

public sealed class AccountTests
{
    [Fact]
    public void PasswordHasher_HashAndVerify_AcceptsOnlyTheOriginalPassword()
    {
        var hash = PasswordHasher.Hash("rahasia-candi");
        Assert.StartsWith("pbkdf2-sha256$1000$", hash);
        Assert.NotEqual(hash, PasswordHasher.Hash("rahasia-candi")); // Fresh salt each time.
        Assert.True(PasswordHasher.Verify("rahasia-candi", hash));
        Assert.False(PasswordHasher.Verify("rahasia-candI", hash));
        Assert.False(PasswordHasher.Verify("rahasia-candi", "plain"));
        Assert.False(PasswordHasher.Verify("rahasia-candi", "pbkdf2-sha256$0$AAAA$AAAA"));
        Assert.False(PasswordHasher.Verify("rahasia-candi", "pbkdf2-sha256$1000$not base64$AAAA"));
        Assert.False(PasswordHasher.Verify("rahasia-candi", "pbkdf2-sha256$999999999$AAAA$AAAA"));
    }

    [Fact]
    public async Task Register_InvalidOrTakenNames_AreRejectedWithoutCreatingAccounts()
    {
        using var database = new TempDatabase();
        var accounts = new AccountService(await database.Store(), new Clock());
        Assert.Equal("name", (await accounts.Register("ab", "password-1")).Error);
        Assert.Equal("name", (await accounts.Register("has space", "password-1")).Error);
        Assert.Equal("name", (await accounts.Register(new string('a', 21), "password-1")).Error);
        Assert.Equal("name", (await accounts.Register(null, "password-1")).Error);
        Assert.Equal("password", (await accounts.Register("Ariana", "short")).Error);
        Assert.Equal("password", (await accounts.Register("Ariana", new string('x', 129))).Error);
        var (grant, error) = await accounts.Register("Ariana", "password-1");
        Assert.Null(error);
        Assert.Equal("Ariana", grant!.Name);
        Assert.Equal("taken", (await accounts.Register("ARIANA", "password-2")).Error); // Names are unique ignoring case.
        Assert.Null(await accounts.Login("ariana", "password-2"));
    }

    [Fact]
    public async Task Login_Sessions_AuthenticateExpireAndLogOut()
    {
        using var database = new TempDatabase();
        var clock = new Clock();
        var accounts = new AccountService(await database.Store(), clock);
        var first = (await accounts.Register("Mischa", "password-1")).Grant!;
        Assert.Equal("Mischa", (await accounts.Authenticate(first.Token))!.Name);
        Assert.Null(await accounts.Login("Mischa", "wrong-password"));
        Assert.Null(await accounts.Login("nobody", "password-1"));
        Assert.Null(await accounts.Login(null, null));
        var second = (await accounts.Login("mischa", "password-1"))!;
        Assert.NotEqual(first.Token, second.Token);
        Assert.Null(await accounts.Authenticate("not-a-token"));
        Assert.Null(await accounts.Authenticate(null));
        Assert.Null(await accounts.Authenticate(new string('x', 500)));

        await accounts.Logout(first.Token);
        Assert.Null(await accounts.Authenticate(first.Token));
        Assert.NotNull(await accounts.Authenticate(second.Token));

        clock.Now += AccountService.SessionLifetime + TimeSpan.FromSeconds(1);
        Assert.Null(await accounts.Authenticate(second.Token));
        Assert.Equal(1, await accounts.PurgeExpired());
    }

    [Fact]
    public async Task Login_MoreThanMaxSessions_DropsTheOldest()
    {
        using var database = new TempDatabase();
        var clock = new Clock();
        var accounts = new AccountService(await database.Store(), clock);
        var tokens = new List<string> { (await accounts.Register("Fadhila", "password-1")).Grant!.Token };
        for (var i = 0; i < AccountService.MaxSessions; i++) { clock.Now += TimeSpan.FromSeconds(1); tokens.Add((await accounts.Login("Fadhila", "password-1"))!.Token); }
        Assert.Null(await accounts.Authenticate(tokens[0]));
        foreach (var token in tokens.Skip(1)) Assert.NotNull(await accounts.Authenticate(token));
    }

    [Fact]
    public async Task Store_TokensAndPasswords_AreNeverStoredInTheClear()
    {
        using var database = new TempDatabase();
        var accounts = new AccountService(await database.Store(), new Clock());
        var grant = (await accounts.Register("Syuba", "password-xyz")).Grant!;
        await using var connection = new SqliteConnection(database.ConnectionString); await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT password_hash FROM accounts) || '|' || (SELECT token_hash FROM sessions);";
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("password-xyz", stored);
        Assert.DoesNotContain(grant.Token, stored);
    }

    [Fact]
    public async Task Migrate_RunTwiceOrAgainstNewerSchema_IsIdempotentOrRefused()
    {
        using var database = new TempDatabase();
        var store = await database.Store();
        await store.Migrate();
        Assert.True(await store.CreateAccount("id-1", "Name", "name", "hash", 1));
        Assert.False(await store.CreateAccount("id-2", "NAME", "name", "hash", 1));
        await using (var connection = new SqliteConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO schema_version (version) VALUES (99);";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(store.Migrate);
    }

    [Fact]
    public async Task RecordMatch_ResultsAndProfile_AggregatePerModeAndIgnoreDuplicates()
    {
        using var database = new TempDatabase();
        var store = await database.Store();
        Assert.True(await store.CreateAccount("a", "Ana", "ana", "hash", 100));
        Assert.True(await store.CreateAccount("b", "Budi", "budi", "hash", 100));
        var won = new FinishedMatch("m1", GameMode.Rts, true, 4800, [new("a", "gajah-mada"), new("b", "republik")]);
        await store.RecordMatch(won, 1000);
        await store.RecordMatch(won with { Won = false }, 2000); // Same ID: the first record stands.
        await store.RecordMatch(new("m2", GameMode.Moba, false, 900, [new("a", "cut-nyak-dien")]), 3000);
        await store.RecordMatch(new("m3", GameMode.Rts, true, 10, []), 4000); // Guests only: nothing to store.

        var ana = (await store.GetProfile("a"))!;
        Assert.Equal(("Ana", 100L, 2, 1), (ana.Name, ana.CreatedAt, ana.Played, ana.Wins));
        Assert.Equal([new ModeRecord(GameMode.Rts, 1, 1), new ModeRecord(GameMode.Moba, 1, 0)], ana.Modes);
        Assert.Equal([new ResultView(GameMode.Moba, "cut-nyak-dien", false, 90, 3000), new ResultView(GameMode.Rts, "gajah-mada", true, 480, 1000)], ana.Recent);
        var budi = (await store.GetProfile("b"))!;
        Assert.Equal((1, 1), (budi.Played, budi.Wins));
        Assert.Null(await store.GetProfile("nobody"));
    }

    [Fact]
    public async Task BackupRestore_AfterDataLoss_BringsAccountsBack()
    {
        using var database = new TempDatabase();
        var accounts = new AccountService(await database.Store(), new Clock());
        var grant = (await accounts.Register("Penjaga", "password-1")).Grant!;
        var backup = Path.Combine(Path.GetTempPath(), $"nusantara-backup-{Guid.NewGuid():N}.db");
        try
        {
            Assert.True(await DatabaseTools.Backup(database.ConnectionString, backup) > 0);
            Assert.False(File.Exists(backup + "-wal") || File.Exists(backup + "-shm")); // One self-contained file.
            await Assert.ThrowsAsync<IOException>(() => DatabaseTools.Backup(database.ConnectionString, backup)); // Never overwrites.
            await using (var connection = new SqliteConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var wipe = connection.CreateCommand(); wipe.CommandText = "DELETE FROM sessions; DELETE FROM accounts;";
                await wipe.ExecuteNonQueryAsync();
            }
            Assert.Null(await accounts.Login("Penjaga", "password-1"));
            await DatabaseTools.Restore(database.ConnectionString, backup);
            Assert.NotNull(await accounts.Login("Penjaga", "password-1"));
            Assert.NotNull(await accounts.Authenticate(grant.Token));

            var junk = backup + ".txt"; await File.WriteAllTextAsync(junk, "not a database");
            try { await Assert.ThrowsAnyAsync<Exception>(() => DatabaseTools.Restore(database.ConnectionString, junk)); Assert.NotNull(await accounts.Login("Penjaga", "password-1")); }
            finally { File.Delete(junk); }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(backup); }
    }
}

public sealed class MatchmakingTests
{
    private static QueueTicket Ticket(string connection, string account, GameMode mode = GameMode.Rts, long at = 1000) => new(connection, account, (int)mode, "gajah-mada", at);

    [Fact]
    public async Task Enqueue_SecondPlayer_SeatsBothInOneNewRoom()
    {
        var rooms = new RoomRegistry();
        var matchmaker = new Matchmaker(new InMemoryQueueStore(), rooms, new PlatformOptions());
        var (waiting, none) = await matchmaker.Enqueue(Ticket("c1", "ana"));
        Assert.Equal(new QueueStatus(1, 2), waiting);
        Assert.Empty(none);
        Assert.Equal(0, rooms.RoomCount);
        var (other, alone) = await matchmaker.Enqueue(Ticket("c9", "zed", GameMode.Moba)); // A different mode never matches.
        Assert.Equal(new QueueStatus(1, 2), other);
        Assert.Empty(alone);

        var (status, notices) = await matchmaker.Enqueue(Ticket("c2", "budi"));
        Assert.Equal(0, status.Waiting);
        Assert.Equal(["c1", "c2"], notices.Select(n => n.Connection));
        Assert.All(notices, n => Assert.Equal(Matchmaker.Matched, n.Event));
        var seats = notices.Select(n => (RoomJoined)n.Payload).ToArray();
        Assert.Equal(seats[0].Code, seats[1].Code);
        Assert.Equal([0, 1], seats.Select(s => s.Owner));
        Assert.All(seats, s => Assert.False(string.IsNullOrEmpty(s.Token)));
        Assert.Equal(1, rooms.RoomCount);
        Assert.True(rooms.InRoom("c1") && rooms.InRoom("c2"));
        Assert.Equal((1, 2, 0), rooms.Census());
    }

    [Fact]
    public async Task Enqueue_SameAccountOnAnotherConnection_ReplacesTheOldTicket()
    {
        var queue = new InMemoryQueueStore();
        var matchmaker = new Matchmaker(queue, new RoomRegistry(), new PlatformOptions());
        await matchmaker.Enqueue(Ticket("old", "ana"));
        var (status, notices) = await matchmaker.Enqueue(Ticket("new", "ana"));
        Assert.Equal(1, status.Waiting); // One account can never be matched with itself.
        var notice = Assert.Single(notices);
        Assert.Equal(("old", Matchmaker.Closed, "replaced"), (notice.Connection, notice.Event, (string)notice.Payload));
        Assert.Equal(1, await queue.Count());
    }

    [Fact]
    public async Task Enqueue_PartySizeThree_WaitsForTheThirdAndClampsToModeCapacity()
    {
        var rooms = new RoomRegistry();
        var matchmaker = new Matchmaker(new InMemoryQueueStore(), rooms, new PlatformOptions { PartySize = 3 });
        Assert.Equal(new QueueStatus(1, 3), (await matchmaker.Enqueue(Ticket("c1", "a"))).Status);
        Assert.Equal(new QueueStatus(2, 3), (await matchmaker.Enqueue(Ticket("c2", "b"))).Status);
        var (_, notices) = await matchmaker.Enqueue(Ticket("c3", "c"));
        Assert.Equal(3, notices.Count(n => n.Event == Matchmaker.Matched));
        Assert.Equal((1, 3, 0), rooms.Census());
        Assert.Equal(5, Matchmaker.PartySize(new PlatformOptions { PartySize = 50 }, GameMode.Moba));
        Assert.Equal(10, Matchmaker.PartySize(new PlatformOptions { PartySize = 50 }, GameMode.Rts));
        Assert.Equal(2, Matchmaker.PartySize(new PlatformOptions { PartySize = 0 }, GameMode.Rts));
    }

    [Fact]
    public async Task Enqueue_PartnerAlreadyInARoom_KeepsTheSurvivorWaiting()
    {
        var rooms = new RoomRegistry();
        var queue = new InMemoryQueueStore();
        var matchmaker = new Matchmaker(queue, rooms, new PlatformOptions());
        await matchmaker.Enqueue(Ticket("c1", "ana"));
        rooms.Enter("c1", "Rts", "gajah-mada", true); // The first player started a room by hand while queued.
        var (status, notices) = await matchmaker.Enqueue(Ticket("c2", "budi"));
        Assert.Equal(1, status.Waiting);
        var notice = Assert.Single(notices);
        Assert.Equal(("c1", Matchmaker.Closed, "unavailable"), (notice.Connection, notice.Event, (string)notice.Payload));
        Assert.False(rooms.InRoom("c2"));
        Assert.Equal(1, rooms.RoomCount); // Only the hand-made room; the half-formed one was undone.
        Assert.Equal(1, await queue.Count());
    }

    [Fact]
    public async Task Enqueue_UnknownHeroModeOrSeatedConnection_NeverQueues()
    {
        var rooms = new RoomRegistry();
        var queue = new InMemoryQueueStore();
        var matchmaker = new Matchmaker(queue, rooms, new PlatformOptions());
        await Assert.ThrowsAsync<ArgumentException>(() => matchmaker.Enqueue(new("c1", "ana", 0, "nobody", 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => matchmaker.Enqueue(new("c1", "ana", 42, "gajah-mada", 1)));
        rooms.Enter("c1", "Rts", "gajah-mada", true);
        Assert.Equal(0, (await matchmaker.Enqueue(Ticket("c1", "ana"))).Status.Waiting);
        Assert.Equal(0, await queue.Count());
    }

    [Fact]
    public async Task CancelAndExpire_RemoveTicketsAndReportOnlyExpiredOnes()
    {
        var queue = new InMemoryQueueStore();
        var matchmaker = new Matchmaker(queue, new RoomRegistry(), new PlatformOptions { TicketSeconds = 120 });
        await matchmaker.Enqueue(Ticket("c1", "ana", GameMode.Rts, at: 1000));
        await matchmaker.Enqueue(Ticket("c2", "budi", GameMode.Moba, at: 1100));
        Assert.True(await matchmaker.Cancel("c1"));
        Assert.False(await matchmaker.Cancel("c1"));
        Assert.Empty(await matchmaker.Expire(1220));
        var notice = Assert.Single(await matchmaker.Expire(1221));
        Assert.Equal(("c2", Matchmaker.Closed, "expired"), (notice.Connection, notice.Event, (string)notice.Payload));
        Assert.Equal(0, await queue.Count());
    }
}

public sealed class RoomPlatformTests
{
    [Fact]
    public void Match_TenPlayersJoinCoop_EleventhIsRejectedAndHeroesDoNotOverlap()
    {
        var match = new Match(GameMode.Rts, "gajah-mada");
        for (var owner = 1; owner < Match.MaxPlayers; owner++) match.AddPlayer(owner, Catalog.Heroes[owner % Catalog.Heroes.Count].Id);
        Assert.Throws<InvalidOperationException>(() => match.AddPlayer(10, "gajah-mada"));
        Assert.Throws<InvalidOperationException>(() => match.AddPlayer(3, "gajah-mada"));
        var heroes = match.Snapshot().Units.Where(u => u.Kind == "hero").ToArray();
        Assert.Equal(10, heroes.Length);
        Assert.Equal(10, heroes.Select(h => (h.X, h.Z)).Distinct().Count());
        Assert.All(heroes, h => Assert.InRange(h.X, 7, 93));
        foreach (var hero in heroes) Assert.True(match.Execute(hero.Owner, new(CommandKind.Move, 20 + hero.Owner * 2, 50, [hero.Id])));
        for (var i = 0; i < 300; i++) match.Advance();
        Assert.Equal(MatchStatus.Playing, match.Status);
        Assert.Equal(10, match.Snapshot().Units.Count(u => u.Kind == "hero"));
    }

    [Fact]
    public void Match_FivePlayersJoinMoba_ReplaceAllyChampionsAndSixthIsRejected()
    {
        var match = new Match(GameMode.Moba, "gajah-mada");
        for (var owner = 1; owner < 5; owner++) match.AddPlayer(owner, "republik");
        Assert.Throws<InvalidOperationException>(() => match.AddPlayer(5, "republik"));
        var units = match.Snapshot().Units;
        Assert.Equal(5, units.Count(u => u.Kind == "hero"));
        Assert.Equal(0, units.Count(u => u.Kind == "champion" && u.Team == 0));
        Assert.Equal(5, units.Count(u => u.Kind == "champion" && u.Team == 1));
        match.RemovePlayer(4);
        Assert.Equal(1, match.Snapshot().Units.Count(u => u.Kind == "champion" && u.Team == 0));
    }

    [Fact]
    public void Registry_FullRooms_RejectTheNextPlayerPerMode()
    {
        var rooms = new RoomRegistry();
        var rts = rooms.Enter("r0", "Rts", "gajah-mada", true);
        for (var i = 1; i < 10; i++) Assert.Equal(i, rooms.Enter($"r{i}", rts.Code, "republik", false).Owner);
        Assert.Throws<HubException>(() => rooms.Enter("r10", rts.Code, "republik", false));
        var moba = rooms.Enter("m0", "Moba", "gajah-mada", true);
        for (var i = 1; i < 5; i++) rooms.Enter($"m{i}", moba.Code, "republik", false);
        Assert.Throws<HubException>(() => rooms.Enter("m5", moba.Code, "republik", false));
        Assert.Equal((2, 15, 0), rooms.Census());
    }

    [Fact]
    public void Registry_MatchEnds_ReportsSignedInSeatsExactlyOnce()
    {
        var rooms = new RoomRegistry();
        var created = rooms.Enter("a", "Rts", "gajah-mada", true, "account-a");
        rooms.Enter("b", created.Code, "republik", false);               // A guest.
        rooms.Enter("c", created.Code, "cut-nyak-dien", false, "account-c");
        Assert.Empty(rooms.TakeFinished());
        // All three heroes march on the enemy base, which ends the match one way or the other.
        FinishedMatch[] finished = [];
        for (var i = 0; i < 6000 && finished.Length == 0; i++)
        {
            if (i % 20 == 0) foreach (var connection in new[] { "a", "b", "c" }) rooms.Command(connection, new(CommandKind.Move, 86, 86));
            rooms.Advance();
            finished = rooms.TakeFinished();
        }
        var match = Assert.Single(finished);
        Assert.Equal(GameMode.Rts, match.Mode);
        Assert.True(match.Ticks > 0);
        Assert.Equal([new SeatResult("account-a", "gajah-mada"), new SeatResult("account-c", "cut-nyak-dien")], match.Players);
        for (var i = 0; i < 20; i++) rooms.Advance();
        Assert.Empty(rooms.TakeFinished());

        // The same match in a room of guests leaves nothing to record.
        var guests = new RoomRegistry();
        guests.Enter("g", "Rts", "gajah-mada", true);
        for (var i = 0; i < match.Ticks + 600; i++)
        {
            if (i % 20 == 0) guests.Command("g", new(CommandKind.Move, 86, 86));
            guests.Advance();
            Assert.Empty(guests.TakeFinished());
        }
    }
}

/// <summary>The real server pipeline on loopback: HTTP API, session authentication, hub and the Orleans lobby.</summary>
public sealed class PlatformHostTests
{
    internal static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal static async Task<(WebApplication App, string Url, TempDatabase Database)> Start(params (string Key, string Value)[] settings)
    {
        var database = new TempDatabase();
        var values = new Dictionary<string, string?>
        {
            ["Platform:ConnectionString"] = database.ConnectionString,
            ["Platform:SiloPort"] = FreePort().ToString(), ["Platform:GatewayPort"] = FreePort().ToString(),
            ["Platform:AuthPerMinute"] = "1000"
        };
        foreach (var (key, value) in settings) values[key] = value;
        var app = ServerHost.Build(["--urls", "http://127.0.0.1:0"], ui: false, builder =>
        {
            builder.Logging.ClearProviders();
            builder.Configuration.AddInMemoryCollection(values);
        });
        await app.StartAsync();
        return (app, app.Urls.First(), database);
    }

    internal static async Task<SessionGrant> Register(HttpClient http, string name)
    {
        var response = await http.PostAsJsonAsync("/api/account/register", new Credentials(name, "password-1"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SessionGrant>())!;
    }

    internal static async Task<HubConnection> Hub(string url, string? token)
    {
        var hub = new HubConnectionBuilder().WithUrl(url + "/hubs/battle", options => { if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token); }).Build();
        await hub.StartAsync();
        return hub;
    }

    [Fact]
    public async Task Api_RegisterLoginProfileLogout_EnforcesSessions()
    {
        var (app, url, database) = await Start();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/account/profile")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/account/register", new Credentials("x", "password-1"))).StatusCode);
            var grant = await Register(http, "Ariana");
            Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/account/register", new Credentials("ariana", "password-1"))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsJsonAsync("/api/account/login", new Credentials("Ariana", "wrong-password"))).StatusCode);
            var login = await (await http.PostAsJsonAsync("/api/account/login", new Credentials("Ariana", "password-1"))).Content.ReadFromJsonAsync<SessionGrant>();

            using var profile = new HttpRequestMessage(HttpMethod.Get, "/api/account/profile");
            profile.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
            var body = await (await http.SendAsync(profile)).Content.ReadFromJsonAsync<Profile>();
            Assert.Equal(("Ariana", 0), (body!.Name, body.Played));

            using var forged = new HttpRequestMessage(HttpMethod.Get, "/api/account/profile");
            forged.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token + "x");
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(forged)).StatusCode);

            using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/account/logout");
            logout.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            Assert.Equal(HttpStatusCode.NoContent, (await http.SendAsync(logout)).StatusCode);
            using var after = new HttpRequestMessage(HttpMethod.Get, "/api/account/profile");
            after.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.SendAsync(after)).StatusCode);
            Assert.NotEqual(grant.Token, login.Token);

            var status = await http.GetFromJsonAsync<ServerStatus>("/api/status");
            Assert.Equal((ServerHost.Version, 0, 0), (status!.Version, status.Rooms, status.Queued));
            Assert.Contains(ServerHost.Version, await http.GetStringAsync("/health"));
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    [Fact]
    public async Task Api_TooManyLoginAttempts_AreRateLimited()
    {
        var (app, url, database) = await Start(("Platform:AuthPerMinute", "3"));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var codes = new List<HttpStatusCode>();
            for (var i = 0; i < 5; i++) codes.Add((await http.PostAsJsonAsync("/api/account/login", new Credentials("nobody", "password-1"))).StatusCode);
            Assert.Equal([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.TooManyRequests], codes);
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    [Fact]
    public async Task Hub_Queue_NeedsAnAccountAndMatchesTwoSignedInPlayers()
    {
        var (app, url, database) = await Start();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var ana = await Register(http, "Ana"); var budi = await Register(http, "Budi");
            await using var guest = await Hub(url, null);
            var refused = await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<QueueStatus>("Queue", "Rts", "gajah-mada"));
            Assert.Contains("Sign in", refused.Message);
            await using var expired = await Hub(url, "not-a-real-token");
            await Assert.ThrowsAsync<HubException>(() => expired.InvokeAsync<QueueStatus>("Queue", "Rts", "gajah-mada"));
            // Guests can still play by room code on a server that does not require accounts.
            Assert.Equal(0, (await guest.InvokeAsync<RoomJoined>("Create", "Rts", "gajah-mada")).Owner);

            await using var first = await Hub(url, ana.Token); await using var second = await Hub(url, budi.Token);
            var firstSeat = new TaskCompletionSource<RoomJoined>(); var secondSeat = new TaskCompletionSource<RoomJoined>();
            first.On<RoomJoined>("Matched", joined => firstSeat.TrySetResult(joined));
            second.On<RoomJoined>("Matched", joined => secondSeat.TrySetResult(joined));
            await Assert.ThrowsAsync<HubException>(() => first.InvokeAsync<QueueStatus>("Queue", "Chess", "gajah-mada"));
            await Assert.ThrowsAsync<HubException>(() => first.InvokeAsync<QueueStatus>("Queue", "Rts", "nobody"));
            Assert.Equal(new QueueStatus(1, 2), await first.InvokeAsync<QueueStatus>("Queue", "Rts", "gajah-mada"));
            Assert.Equal(1, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued);
            await second.InvokeAsync<QueueStatus>("Queue", "Rts", "republik");
            var seats = await Task.WhenAll(firstSeat.Task, secondSeat.Task).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(seats[0].Code, seats[1].Code);
            Assert.Equal([0, 1], seats.Select(s => s.Owner).Order());
            Assert.True(await first.InvokeAsync<bool>("Command", new GameCommand(CommandKind.Guard)));
            var status = await http.GetFromJsonAsync<ServerStatus>("/api/status");
            Assert.Equal((2, 3, 0), (status!.Rooms, status.Players, status.Queued));
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    [Fact]
    public async Task Hub_QueuedPlayerDisconnectsOrCancels_LeavesTheQueue()
    {
        var (app, url, database) = await Start();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var ana = await Register(http, "Ana"); var budi = await Register(http, "Budi");
            var first = await Hub(url, ana.Token);
            await first.InvokeAsync<QueueStatus>("Queue", "Moba", "gajah-mada");
            await first.DisposeAsync();
            for (var i = 0; i < 100 && (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued > 0; i++) await Task.Delay(50);
            Assert.Equal(0, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued);
            await using var second = await Hub(url, budi.Token);
            Assert.Equal(new QueueStatus(1, 2), await second.InvokeAsync<QueueStatus>("Queue", "Moba", "republik"));
            await second.InvokeAsync("CancelQueue");
            Assert.Equal(0, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued);
            Assert.Equal(0, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Rooms);
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        for (var i = 0; i < 400; i++) { if (condition()) return; await Task.Delay(50); }
        Assert.Fail($"Timed out waiting for {what}.");
    }

    [Fact]
    public async Task Client_SignInFindMatchAndResume_WorksAgainstTheRealServer()
    {
        var (app, url, database) = await Start();
        try
        {
            await using var ana = new GameClient(); await using var budi = new GameClient();
            Assert.Equal("password", await ana.SignIn(url, "Ana", "short", register: true));
            Assert.Equal("name", await ana.SignIn(url, "a b", "password-1", register: true));
            Assert.Null(await ana.SignIn(url, "Ana", "password-1", register: true));
            Assert.Equal("taken", await budi.SignIn(url, "ana", "password-1", register: true));
            Assert.Equal("credentials", await budi.SignIn(url, "Ana", "wrong-password", register: false));
            Assert.Null(budi.AccountName);
            Assert.Equal("network", await budi.SignIn("http://127.0.0.1:" + FreePort(), "Budi", "password-1", register: true));
            await Assert.ThrowsAsync<ArgumentException>(() => budi.SignIn("ftp://example", "Budi", "password-1", register: true));
            await Assert.ThrowsAsync<InvalidOperationException>(() => budi.FindMatch(GameMode.Rts, "republik"));
            Assert.Null(await budi.SignIn(url, "Budi", "password-1", register: true));
            Assert.Equal(("Ana", 0), (ana.Profile!.Name, ana.Profile.Played));

            await ana.FindMatch(GameMode.Rts, "gajah-mada");
            Assert.True(ana.Queued);
            Assert.Equal(new QueueStatus(1, 2), ana.Queue);
            Assert.Null(ana.View);
            await budi.FindMatch(GameMode.Rts, "republik");
            await Until(() => ana.RoomCode is not null && budi.RoomCode is not null, "both players to be matched");
            Assert.Equal(ana.RoomCode, budi.RoomCode);
            Assert.False(ana.Queued || budi.Queued);
            Assert.Equal([0, 1], new[] { ana.Owner, budi.Owner }.Order());
            await Until(() => ana.View!.Tick > 2 && budi.View!.Units.Count(u => u.Kind == "hero") == 2, "shared snapshots");
            Assert.True(await budi.Command(new(CommandKind.Guard)));

            // A stored session resumes on a fresh client; a forged one does not.
            var session = ana.Session!.Value;
            await using var later = new GameClient();
            Assert.False(await later.Resume(session.Server, session.Token + "x"));
            Assert.Null(later.Session);
            Assert.True(await later.Resume(session.Server, session.Token));
            Assert.Equal("Ana", later.AccountName);
            await later.SignOut();
            Assert.Null(later.AccountName);
            await using var stale = new GameClient();
            Assert.False(await stale.Resume(session.Server, session.Token)); // Signing out revoked it on the server.
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    [Fact]
    public async Task Client_AccountFromOneServer_IsNotAcceptedByAnother()
    {
        var (home, homeUrl, homeDatabase) = await Start();
        var (other, otherUrl, otherDatabase) = await Start(("Platform:RequireAccount", "true"));
        try
        {
            await using var client = new GameClient();
            Assert.Null(await client.SignIn(homeUrl, "Ana", "password-1", register: true));
            // The other server requires an account and has never seen this one, so the client is a guest there.
            await Assert.ThrowsAsync<HubException>(() => client.Connect(otherUrl, null, GameMode.Rts, "gajah-mada"));
            Assert.Equal("Ana", client.AccountName);
            await client.Connect(homeUrl, null, GameMode.Rts, "gajah-mada");
            Assert.NotNull(client.RoomCode);
        }
        finally { await home.DisposeAsync(); await other.DisposeAsync(); homeDatabase.Dispose(); otherDatabase.Dispose(); }
    }

    [Fact]
    public async Task Client_QueueWhileServerStops_ReportsTheLossAndLeavesTheQueue()
    {
        var (app, url, database) = await Start();
        try
        {
            await using var client = new GameClient();
            Assert.Null(await client.SignIn(url, "Ana", "password-1", register: true));
            await client.FindMatch(GameMode.Moba, "gajah-mada");
            Assert.True(client.Queued);
            await client.CancelQueue();
            Assert.False(client.Queued);
            await client.FindMatch(GameMode.Moba, "gajah-mada");
            await app.StopAsync();
            await Until(() => client.NetworkError is not null, "the lost queue to be reported");
            Assert.False(client.Queued);
            Assert.False(client.Reconnecting);
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }

    [Fact]
    public async Task Hub_RequireAccount_RefusesGuestsEverywhereButLetsMembersIn()
    {
        var (app, url, database) = await Start(("Platform:RequireAccount", "true"));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var ana = await Register(http, "Ana");
            await using var member = await Hub(url, ana.Token);
            var room = await member.InvokeAsync<RoomJoined>("Create", "Rts", "gajah-mada");
            await using var guest = await Hub(url, null);
            await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<RoomJoined>("Create", "Rts", "gajah-mada"));
            await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<RoomJoined>("Join", room.Code, "republik"));
            await Assert.ThrowsAsync<HubException>(() => guest.InvokeAsync<RoomJoined>("Watch", room.Code));
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }
}
