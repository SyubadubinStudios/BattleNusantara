using System.Data.Common;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using BattleNusantara.Server.Components;
using BattleNusantara.UI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Npgsql;
using StackExchange.Redis;

namespace BattleNusantara.Server.Platform;

/// <summary>The "Platform" configuration section.</summary>
public sealed class PlatformOptions
{
    /// <summary>"sqlite" (embedded, the tested default) or "postgres".</summary>
    public string Database { get; set; } = "sqlite";
    public string ConnectionString { get; set; } = "Data Source=data/nusantara.db";
    /// <summary>"memory" (single host, the tested default) or "redis".</summary>
    public string Queue { get; set; } = "memory";
    public string Redis { get; set; } = "localhost:6379";
    /// <summary>When true, creating, joining and watching rooms need a signed-in account. Matchmaking always does.</summary>
    public bool RequireAccount { get; set; }
    public int PartySize { get; set; } = 2;
    public int TicketSeconds { get; set; } = 120;
    /// <summary>Register and login attempts allowed per client address per minute.</summary>
    public int AuthPerMinute { get; set; } = 30;
    public int SiloPort { get; set; } = 11111;
    public int GatewayPort { get; set; } = 30000;
    /// <summary>Optional language-model voice for the Keeper NPC; see <see cref="NpcOptions"/>.</summary>
    public NpcOptions Npc { get; set; } = new();
}

public sealed record Credentials(string? Name, string? Password);

/// <summary>Tick loop timings, read by /api/status and the load test.</summary>
public sealed class LoopMetrics
{
    private long ticks, overruns, totalMicros, worstMicros;
    public long Ticks => Interlocked.Read(ref ticks);
    /// <summary>Ticks whose simulation and snapshot delivery took longer than the 100 ms step.</summary>
    public long Overruns => Interlocked.Read(ref overruns);
    public double MeanMs => Ticks == 0 ? 0 : Interlocked.Read(ref totalMicros) / 1000d / Ticks;
    public double WorstMs => Interlocked.Read(ref worstMicros) / 1000d;
    public void Record(TimeSpan elapsed)
    {
        var micros = (long)elapsed.TotalMicroseconds;
        Interlocked.Increment(ref ticks); Interlocked.Add(ref totalMicros, micros);
        if (elapsed.TotalMilliseconds > 100) Interlocked.Increment(ref overruns);
        long seen; while (micros > (seen = Interlocked.Read(ref worstMicros)) && Interlocked.CompareExchange(ref worstMicros, micros, seen) != seen) { }
    }
    public void Reset() { Interlocked.Exchange(ref ticks, 0); Interlocked.Exchange(ref overruns, 0); Interlocked.Exchange(ref totalMicros, 0); Interlocked.Exchange(ref worstMicros, 0); }
}

/// <summary>Accepts "Authorization: Bearer token", or access_token in the query for hub connections.</summary>
public sealed class SessionAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, AccountService accounts)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    public static string? Token(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header[7..].Trim();
        return request.Path.StartsWithSegments("/hubs") ? request.Query["access_token"].ToString() : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Token(Request);
        if (string.IsNullOrEmpty(token)) return AuthenticateResult.NoResult();
        var account = await accounts.Authenticate(token);
        if (account is null) return AuthenticateResult.Fail("Invalid or expired session.");
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id), new Claim(ClaimTypes.Name, account.Name)], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}

/// <summary>Removes expired sessions once an hour.</summary>
public sealed class SessionSweeper(AccountService accounts, ILogger<SessionSweeper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            do
            {
                try { await accounts.PurgeExpired(); }
                catch (DbException exception) { logger.LogWarning(exception, "Session cleanup failed"); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

public static class ServerHost
{
    public const string Version = "0.13.0";

    /// <summary>Builds the server. Tests pass <paramref name="ui"/> = false to skip the Blazor UI and its static assets.</summary>
    public static WebApplication Build(string[] args, bool ui = true, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configure?.Invoke(builder);
        var options = builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new();
        if (ui)
        {
            builder.WebHost.UseStaticWebAssets();
            // The UI circuit must carry a saved Adventure read back from browser storage; the game hub accepts only small commands.
            builder.Services.AddRazorComponents().AddInteractiveServerComponents().AddHubOptions(o => o.MaximumReceiveMessageSize = 32 * 1024);
            builder.Services.AddScoped<GameClient>();
        }
        builder.Services.AddSignalR().AddHubOptions<BattleHub>(o => o.MaximumReceiveMessageSize = 4096);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<LoopMetrics>();
        builder.Services.AddSingleton<RoomRegistry>();
        builder.Services.AddSingleton(Store(options, builder.Environment.ContentRootPath));
        builder.Services.AddSingleton<AccountService>();
        builder.Services.AddSingleton<Matchmaker>();
        if (options.Queue.Equals("redis", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.Redis));
            builder.Services.AddSingleton<IQueueStore, RedisQueueStore>();
        }
        else builder.Services.AddSingleton<IQueueStore, InMemoryQueueStore>();
        builder.Services.AddHostedService<BattleLoop>();
        builder.Services.AddHostedService<SessionSweeper>();
        builder.Services.AddAuthentication(SessionAuthHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, SessionAuthHandler>(SessionAuthHandler.SchemeName, null);
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton(options.Npc);
        if (options.Npc.Enabled) builder.Services.AddSingleton<INpcVoice, SemanticKernelVoice>();
        builder.Services.AddSingleton(services => new NpcService(services.GetService<INpcVoice>(), options.Npc, services.GetRequiredService<TimeProvider>(), services.GetRequiredService<ILogger<NpcService>>()));
        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy("npc", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.Npc.PerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            limiter.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.AuthPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        // One silo in this process. Localhost clustering keeps membership in memory; a multi-host deployment needs a real membership provider.
        builder.Host.UseOrleans(silo => silo.UseLocalhostClustering(options.SiloPort, options.GatewayPort));

        var app = builder.Build();
        app.Services.GetRequiredService<IAccountStore>().Migrate().GetAwaiter().GetResult();
        if (ui) app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        if (ui) { app.UseAntiforgery(); app.MapStaticAssets(); }
        app.MapGet("/health", () => Results.Ok(new { status = "ok", game = "Battle of Nusantara", version = Version }));
        MapApi(app);
        app.MapHub<BattleHub>("/hubs/battle");
        if (ui) app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }

    private static void MapApi(WebApplication app)
    {
        var limit = new RequestSizeLimitAttribute(4096);
        app.MapPost("/api/account/register", async (Credentials body, AccountService accounts) =>
        {
            var (grant, error) = await accounts.Register(body.Name, body.Password);
            return grant is not null ? Results.Ok(grant) : error == "taken" ? Results.Conflict(new { error }) : Results.BadRequest(new { error });
        }).RequireRateLimiting("auth").WithMetadata(limit);
        app.MapPost("/api/account/login", async (Credentials body, AccountService accounts) =>
            await accounts.Login(body.Name, body.Password) is { } grant ? Results.Ok(grant) : Results.Unauthorized()).RequireRateLimiting("auth").WithMetadata(limit);
        app.MapPost("/api/account/logout", async (HttpRequest request, AccountService accounts) =>
        {
            await accounts.Logout(SessionAuthHandler.Token(request));
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/account/profile", async (ClaimsPrincipal user, AccountService accounts) =>
            await accounts.Profile(user.FindFirstValue(ClaimTypes.NameIdentifier)!) is { } profile ? Results.Ok(profile) : Results.NotFound()).RequireAuthorization();
        // The Keeper answers fixed topics only; the reply is generated when a model is configured, authored otherwise.
        app.MapGet("/api/npc/ask", async (string? topic, string? lang, NpcService npc) =>
            await npc.Ask(topic, lang) is { } reply ? Results.Ok(reply) : Results.NotFound()).RequireRateLimiting("npc");
        // A short typed question; the text only ever reaches the model as the user message, never the instructions.
        app.MapPost("/api/npc/talk", async (NpcQuestion question, NpcService npc) =>
            await npc.Talk(question.Npc, question.Text, question.Lang) is { } reply ? Results.Ok(reply) : Results.BadRequest()).RequireRateLimiting("npc");
        // Counts and timings only: nothing here identifies a player.
        app.MapGet("/api/status", async (RoomRegistry rooms, IQueueStore queue, LoopMetrics metrics) =>
        {
            var (count, players, spectators) = rooms.Census();
            return Results.Ok(new ServerStatus(Version, count, players, spectators, await queue.Count(), metrics.Ticks, metrics.Overruns, metrics.MeanMs, metrics.WorstMs));
        });
    }

    public static IAccountStore Store(PlatformOptions options, string contentRoot)
    {
        if (options.Database.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            // Tested against PostgreSQL 17 by PostgresStoreTests when NUSANTARA_TEST_POSTGRES is set.
            var source = NpgsqlDataSource.Create(options.ConnectionString);
            return new SqlAccountStore(source.CreateConnection, sqlite: false);
        }
        if (!options.Database.Equals("sqlite", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Platform:Database must be \"sqlite\" or \"postgres\".");
        var text = SqliteConnectionString(options, contentRoot);
        return new SqlAccountStore(() => new SqliteConnection(text), sqlite: true);
    }

    /// <summary>Resolves a relative SQLite file against the content root and makes sure its folder exists.</summary>
    public static string SqliteConnectionString(PlatformOptions options, string contentRoot)
    {
        var parsed = new SqliteConnectionStringBuilder(options.ConnectionString);
        if (parsed.DataSource != ":memory:" && !parsed.DataSource.StartsWith("file:", StringComparison.Ordinal))
        {
            parsed.DataSource = Path.GetFullPath(parsed.DataSource, contentRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(parsed.DataSource)!);
        }
        return parsed.ToString();
    }
}

public sealed record ServerStatus(string Version, int Rooms, int Players, int Spectators, int Queued, long Ticks, long Overruns, double MeanTickMs, double WorstTickMs);

/// <summary>Offline maintenance for the embedded database: "backup &lt;file&gt;" and "restore &lt;file&gt;".</summary>
public static class DatabaseTools
{
    public static async Task<int> Run(string command, string file, string[] rest)
    {
        var builder = WebApplication.CreateBuilder(rest);
        var options = builder.Configuration.GetSection("Platform").Get<PlatformOptions>() ?? new();
        if (!options.Database.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("This command handles the embedded SQLite database. For PostgreSQL use scripts/backup.ps1 -Postgres (pg_dump).");
            return 2;
        }
        var live = ServerHost.SqliteConnectionString(options, builder.Environment.ContentRootPath);
        try
        {
            if (command == "backup") { var bytes = await Backup(live, Path.GetFullPath(file)); Console.WriteLine($"Backup written: {Path.GetFullPath(file)} ({bytes} bytes)"); }
            else { await Restore(live, Path.GetFullPath(file)); Console.WriteLine($"Database restored from {Path.GetFullPath(file)}"); }
            return 0;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{command} failed: {exception.Message}");
            return 1;
        }
    }

    /// <summary>Consistent online copy through SQLite's backup API; safe while the server is running.</summary>
    public static async Task<long> Backup(string liveConnection, string file)
    {
        if (File.Exists(file)) throw new IOException($"Refusing to overwrite {file}.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await using (var source = new SqliteConnection(liveConnection))
        await using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        {
            await source.OpenAsync(); await target.OpenAsync();
            source.BackupDatabase(target);
            // Leave one self-contained file: without this the copy inherits WAL mode and grows -wal/-shm companions.
            await using var single = target.CreateCommand(); single.CommandText = "PRAGMA journal_mode = DELETE;";
            await single.ExecuteNonQueryAsync();
        }
        await Verify(file);
        return new FileInfo(file).Length;
    }

    /// <summary>Replaces the live database with a verified backup. Stop the server first.</summary>
    public static async Task Restore(string liveConnection, string file)
    {
        await Verify(file);
        SqliteConnection.ClearAllPools();
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await using var target = new SqliteConnection(liveConnection);
        await source.OpenAsync(); await target.OpenAsync();
        source.BackupDatabase(target);
    }

    private static async Task Verify(string file)
    {
        if (!File.Exists(file)) throw new IOException($"{file} does not exist.");
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check;";
        if ((string?)await check.ExecuteScalarAsync() != "ok") throw new InvalidOperationException($"{file} failed the integrity check.");
        await using var version = connection.CreateCommand(); version.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        var schema = Convert.ToInt64(await version.ExecuteScalarAsync());
        if (schema < 1 || schema > SqlAccountStore.SchemaVersion) throw new InvalidOperationException($"{file} has schema {schema}; this server supports 1 to {SqlAccountStore.SchemaVersion}.");
    }
}
