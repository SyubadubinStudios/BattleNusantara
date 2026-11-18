using System.Net.Http.Json;
using BattleNusantara.Core;
using BattleNusantara.Server;
using BattleNusantara.Server.Platform;
using BattleNusantara.UI;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace BattleNusantara.Tests;

/// <summary>
/// Addresses of real servers for the integration tests: environment variables, or KEY=VALUE lines in
/// ~/.nusantara/test.env. Without them the tests are reported as skipped, never as passed.
/// </summary>
internal static class External
{
    public const string PostgresKey = "NUSANTARA_TEST_POSTGRES", RedisKey = "NUSANTARA_TEST_REDIS";

    public static string? Setting(string key)
    {
        if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) return value;
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nusantara", "test.env");
        if (!File.Exists(file)) return null;
        foreach (var line in File.ReadAllLines(file))
            if (line.StartsWith(key + "=", StringComparison.Ordinal)) return line[(key.Length + 1)..].Trim();
        return null;
    }
}

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (External.Setting(External.PostgresKey) is null) Skip = $"Set {External.PostgresKey} to a PostgreSQL connection string to run this test."; }
}

public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute() { if (External.Setting(External.RedisKey) is null) Skip = $"Set {External.RedisKey} to a Redis-compatible address to run this test."; }
}

/// <summary>A private PostgreSQL schema per test, dropped afterwards.</summary>
internal sealed class TempSchema : IAsyncDisposable
{
    private readonly string admin = External.Setting(External.PostgresKey)!;
    public string Name { get; } = "t_" + Guid.NewGuid().ToString("N");
    public string ConnectionString => new NpgsqlConnectionStringBuilder(admin) { SearchPath = Name }.ToString();

    public static async Task<TempSchema> Create()
    {
        var schema = new TempSchema();
        await schema.Run($"CREATE SCHEMA {schema.Name};");
        return schema;
    }

    public async Task<SqlAccountStore> Store()
    {
        var source = NpgsqlDataSource.Create(ConnectionString);
        var store = new SqlAccountStore(source.CreateConnection, sqlite: false);
        await store.Migrate();
        return store;
    }

    public async Task<object?> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task Run(string sql)
    {
        await using var connection = new NpgsqlConnection(admin); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync() { NpgsqlConnection.ClearAllPools(); await Run($"DROP SCHEMA IF EXISTS {Name} CASCADE;"); }
}

/// <summary>The SQL store and the whole server against a real PostgreSQL server.</summary>
public sealed class PostgresStoreTests
{
    [PostgresFact]
    public async Task Migrate_OnPostgres_CreatesSchemaOnceAndRefusesANewerOne()
    {
        await using var schema = await TempSchema.Create();
        var store = await schema.Store();
        await store.Migrate();
        Assert.Equal((long)SqlAccountStore.SchemaVersion, Convert.ToInt64(await schema.Scalar("SELECT MAX(version) FROM schema_version;")));
        Assert.Equal(1L, Convert.ToInt64(await schema.Scalar("SELECT COUNT(*) FROM schema_version;")));
        Assert.True(await store.CreateAccount("id-1", "Name", "name", "hash", 1));
        Assert.False(await store.CreateAccount("id-2", "NAME", "name", "hash", 1));
        await schema.Scalar("INSERT INTO schema_version (version) VALUES (99);");
        await Assert.ThrowsAsync<InvalidOperationException>(store.Migrate);
    }

    [PostgresFact]
    public async Task Accounts_OnPostgres_RegisterLoginExpireRevokeAndCapSessions()
    {
        await using var schema = await TempSchema.Create();
        var clock = new Clock();
        var accounts = new AccountService(await schema.Store(), clock);
        Assert.Equal("taken", (await accounts.Register("Ariana", "password-1")).Error is null ? (await accounts.Register("ARIANA", "password-2")).Error : "unexpected");
        var tokens = new List<string>();
        for (var i = 0; i <= AccountService.MaxSessions; i++) { clock.Now += TimeSpan.FromSeconds(1); tokens.Add((await accounts.Login("ariana", "password-1"))!.Token); }
        Assert.Null(await accounts.Login("Ariana", "wrong-password"));
        Assert.Null(await accounts.Authenticate(tokens[0])); // Oldest of six logins was dropped (five kept).
        foreach (var token in tokens.Skip(1)) Assert.Equal("Ariana", (await accounts.Authenticate(token))!.Name);
        Assert.Equal((long)AccountService.MaxSessions, Convert.ToInt64(await schema.Scalar("SELECT COUNT(*) FROM sessions;")));
        await accounts.Logout(tokens[1]);
        Assert.Null(await accounts.Authenticate(tokens[1]));
        var stored = (string)(await schema.Scalar("SELECT (SELECT password_hash FROM accounts) || '|' || (SELECT string_agg(token_hash, ',') FROM sessions);"))!;
        Assert.DoesNotContain("password-1", stored);
        Assert.DoesNotContain(tokens[2], stored);
        clock.Now += AccountService.SessionLifetime + TimeSpan.FromSeconds(1);
        Assert.Null(await accounts.Authenticate(tokens[2]));
        Assert.Equal(AccountService.MaxSessions - 1, await accounts.PurgeExpired());
    }

    [PostgresFact]
    public async Task RecordMatch_OnPostgres_AggregatesProfileAndIgnoresDuplicates()
    {
        await using var schema = await TempSchema.Create();
        var store = await schema.Store();
        Assert.True(await store.CreateAccount("a", "Ana", "ana", "hash", 100));
        Assert.True(await store.CreateAccount("b", "Budi", "budi", "hash", 100));
        var won = new FinishedMatch("m1", GameMode.Rts, true, 4800, [new("a", "gajah-mada"), new("b", "republik")]);
        await store.RecordMatch(won, 1000);
        await store.RecordMatch(won with { Won = false }, 2000);
        await store.RecordMatch(new("m2", GameMode.Moba, false, 900, [new("a", "cut-nyak-dien")]), 3000);
        var ana = (await store.GetProfile("a"))!;
        Assert.Equal(("Ana", 100L, 2, 1), (ana.Name, ana.CreatedAt, ana.Played, ana.Wins));
        Assert.Equal([new ModeRecord(GameMode.Rts, 1, 1), new ModeRecord(GameMode.Moba, 1, 0)], ana.Modes);
        Assert.Equal([new ResultView(GameMode.Moba, "cut-nyak-dien", false, 90, 3000), new ResultView(GameMode.Rts, "gajah-mada", true, 480, 1000)], ana.Recent);
        Assert.Null(await store.GetProfile("nobody"));
        // An unknown account violates the foreign key and rolls the whole match back.
        await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => store.RecordMatch(new("m3", GameMode.Rts, true, 10, [new("ghost", "republik")]), 4000));
        Assert.Equal(0L, Convert.ToInt64(await schema.Scalar("SELECT COUNT(*) FROM matches WHERE id = 'm3';")));
    }

    [PostgresFact]
    public async Task Server_OnPostgres_RegistersAccountsAndMatchesPlayersEndToEnd()
    {
        await using var schema = await TempSchema.Create();
        var (app, url, database) = await PlatformHostTests.Start(("Platform:Database", "postgres"), ("Platform:ConnectionString", schema.ConnectionString));
        try
        {
            await using var ana = new GameClient(); await using var budi = new GameClient();
            Assert.Null(await ana.SignIn(url, "Ana", "password-1", register: true));
            Assert.Equal("taken", await budi.SignIn(url, "ANA", "password-1", register: true));
            Assert.Null(await budi.SignIn(url, "Budi", "password-1", register: true));
            await ana.FindMatch(GameMode.Rts, "gajah-mada"); await budi.FindMatch(GameMode.Rts, "republik");
            for (var i = 0; i < 400 && (ana.RoomCode is null || budi.RoomCode is null); i++) await Task.Delay(50);
            Assert.NotNull(ana.RoomCode);
            Assert.Equal(ana.RoomCode, budi.RoomCode);
            Assert.Equal(2L, Convert.ToInt64(await schema.Scalar("SELECT COUNT(*) FROM accounts;")));
            Assert.True(await ana.RefreshProfile());
            Assert.Equal("Ana", ana.Profile!.Name);
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }
}

/// <summary>The Redis queue store against a real Redis-protocol server. One class, so the tests never interleave.</summary>
public sealed class RedisQueueTests
{
    private static async Task<(RedisQueueStore Store, ConnectionMultiplexer Redis, string Prefix)> Open()
    {
        var redis = await ConnectionMultiplexer.ConnectAsync(External.Setting(External.RedisKey)!);
        var prefix = "test-" + Guid.NewGuid().ToString("N");
        return (new RedisQueueStore(redis, prefix), redis, prefix);
    }
    private static QueueTicket Ticket(string connection, string account, GameMode mode = GameMode.Rts, long at = 1000) => new(connection, account, (int)mode, "gajah-mada", at);

    [RedisFact]
    public async Task Queue_OnRedis_EnqueuesReplacesTakesAndRemovesLikeTheInMemoryStore()
    {
        var (store, redis, _) = await Open();
        await using var _redis = redis;
        Assert.Equal((1, null), await store.Enqueue(Ticket("c1", "ana")));
        Assert.Equal((1, null), await store.Enqueue(Ticket("m1", "zed", GameMode.Moba)));
        Assert.Empty(await store.Take((int)GameMode.Rts, 2)); // Fewer than two wait: nothing is removed.
        Assert.Equal(2, await store.Count());
        var (waiting, replaced) = await store.Enqueue(Ticket("c1b", "ana")); // The same account from another connection.
        Assert.Equal((1, "c1"), (waiting, replaced));
        Assert.Equal((2, null), await store.Enqueue(Ticket("c2", "budi")));
        Assert.Equal((3, null), await store.Enqueue(Ticket("c3", "citra")));
        var group = await store.Take((int)GameMode.Rts, 2);
        Assert.Equal(["c1b", "c2"], group.Select(t => t.Connection)); // Oldest first.
        Assert.Equal(["ana", "budi"], group.Select(t => t.Account));
        Assert.Equal(2, await store.Count());
        Assert.True(await store.Remove("c3"));
        Assert.False(await store.Remove("c3"));
        Assert.False(await store.Remove("c2")); // Already taken.
        Assert.Equal(1, await store.Count());
        Assert.True(await store.Remove("m1"));
        Assert.Equal(0, await store.Count());
    }

    [RedisFact]
    public async Task Queue_OnRedis_ExpiresOldTicketsAndDropsOnesWhosePresenceLapsed()
    {
        var (store, redis, prefix) = await Open();
        await using var _redis = redis;
        await store.Enqueue(Ticket("old", "ana", at: 1000));
        await store.Enqueue(Ticket("new", "budi", at: 2000));
        await store.Enqueue(Ticket("gone", "citra", at: 2000));
        // Simulate a crashed host: the ticket's presence key expired but its queue entry stayed behind.
        Assert.True(await redis.GetDatabase().KeyDeleteAsync($"{prefix}:ticket:gone"));
        var expired = await store.Expire(1500);
        Assert.Equal(["old"], expired.Select(t => t.Connection));
        Assert.Equal(1, await store.Count());
        Assert.Empty(await store.Expire(1500));
        await store.Enqueue(Ticket("late", "dewi", at: 2000));
        await redis.GetDatabase().KeyDeleteAsync($"{prefix}:ticket:late");
        var group = await store.Take((int)GameMode.Rts, 2); // Two entries popped, one of them lapsed.
        Assert.Equal(["new"], group.Select(t => t.Connection));
        Assert.Equal(0, await store.Count());
    }

    [RedisFact]
    public async Task Matchmaker_OnRedis_PairsPlayersAndRequeuesAShortGroup()
    {
        var (store, redis, prefix) = await Open();
        await using var _redis = redis;
        var rooms = new RoomRegistry();
        var matchmaker = new Matchmaker(store, rooms, new PlatformOptions());
        Assert.Equal(new QueueStatus(1, 2), (await matchmaker.Enqueue(Ticket("c1", "ana"))).Status);
        var (status, notices) = await matchmaker.Enqueue(Ticket("c2", "budi"));
        Assert.Equal(0, status.Waiting);
        Assert.Equal(["c1", "c2"], notices.Where(n => n.Event == Matchmaker.Matched).Select(n => n.Connection));
        Assert.Equal((1, 2, 0), rooms.Census());
        Assert.Equal(0, await store.Count());

        // A partner whose presence lapsed leaves a short group: the survivor must keep waiting, not be lost.
        await matchmaker.Enqueue(Ticket("c3", "citra"));
        await redis.GetDatabase().KeyDeleteAsync($"{prefix}:ticket:c3");
        var (again, none) = await matchmaker.Enqueue(Ticket("c4", "dewi"));
        Assert.DoesNotContain(none, n => n.Event == Matchmaker.Matched);
        Assert.Equal(1, again.Waiting);
        Assert.Equal(1, await store.Count());
        Assert.False(rooms.InRoom("c4"));
        var (final, matched) = await matchmaker.Enqueue(Ticket("c5", "eka"));
        Assert.Equal(["c4", "c5"], matched.Where(n => n.Event == Matchmaker.Matched).Select(n => n.Connection));
        Assert.Equal(0, final.Waiting);
    }

    [RedisFact]
    public async Task Server_OnRedisQueue_MatchesTwoSignedInPlayersEndToEnd()
    {
        var address = External.Setting(External.RedisKey)!;
        await using (var cleanup = await ConnectionMultiplexer.ConnectAsync(address + ",allowAdmin=true"))
            foreach (var key in new[] { "nusantara:queue:0", "nusantara:queue:1", "nusantara:queue:2" }) await cleanup.GetDatabase().KeyDeleteAsync(key);
        var (app, url, database) = await PlatformHostTests.Start(("Platform:Queue", "redis"), ("Platform:Redis", address));
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var ana = await PlatformHostTests.Register(http, "Ana"); var budi = await PlatformHostTests.Register(http, "Budi");
            await using var first = await PlatformHostTests.Hub(url, ana.Token); await using var second = await PlatformHostTests.Hub(url, budi.Token);
            var firstSeat = new TaskCompletionSource<RoomJoined>(); var secondSeat = new TaskCompletionSource<RoomJoined>();
            first.On<RoomJoined>("Matched", joined => firstSeat.TrySetResult(joined));
            second.On<RoomJoined>("Matched", joined => secondSeat.TrySetResult(joined));
            Assert.Equal(new QueueStatus(1, 2), await first.InvokeAsync<QueueStatus>("Queue", "Moba", "gajah-mada"));
            Assert.Equal(1, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued);
            await second.InvokeAsync<QueueStatus>("Queue", "Moba", "republik");
            var seats = await Task.WhenAll(firstSeat.Task, secondSeat.Task).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(seats[0].Code, seats[1].Code);
            Assert.Equal(0, (await http.GetFromJsonAsync<ServerStatus>("/api/status"))!.Queued);
        }
        finally { await app.DisposeAsync(); database.Dispose(); }
    }
}
