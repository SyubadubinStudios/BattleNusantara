using System.Buffers.Text;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BattleNusantara.Core;

namespace BattleNusantara.Server.Platform;

public sealed record Account(string Id, string Name);
public sealed record SessionGrant(string Token, string Name);
public sealed record ModeRecord(GameMode Mode, int Played, int Wins);
public sealed record ResultView(GameMode Mode, string Hero, bool Won, double Seconds, long FinishedAt);
public sealed record Profile(string Name, long CreatedAt, int Played, int Wins, ModeRecord[] Modes, ResultView[] Recent);
public sealed record SeatResult(string Account, string Hero);
/// <summary>A finished room match and the signed-in players who were seated when it ended.</summary>
public sealed record FinishedMatch(string Id, GameMode Mode, bool Won, long Ticks, SeatResult[] Players);

/// <summary>PBKDF2-SHA256 with a per-password salt. The stored text carries its own iteration count.</summary>
public static class PasswordHasher
{
    /// <summary>Iterations for new hashes. Tests lower this; existing hashes keep the count they were made with.</summary>
    public static int Iterations { get; set; } = 600_000;
    private const int SaltBytes = 16, KeyBytes = 32, MaxIterations = 5_000_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations < 1 || iterations > MaxIterations) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]); var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}

/// <summary>Durable accounts, sessions and match results.</summary>
public interface IAccountStore
{
    Task Migrate();
    /// <summary>False when the name is already taken.</summary>
    Task<bool> CreateAccount(string id, string name, string nameKey, string passwordHash, long now);
    Task<(string Id, string Name, string PasswordHash)?> FindByName(string nameKey);
    /// <summary>Stores a session and drops the account's oldest sessions beyond <paramref name="keep"/>.</summary>
    Task CreateSession(string tokenHash, string accountId, long expiresAt, int keep);
    Task<Account?> FindSession(string tokenHash, long now);
    Task DeleteSession(string tokenHash);
    Task<int> PurgeSessions(long now);
    /// <summary>Idempotent: recording the same match ID twice keeps the first record.</summary>
    Task RecordMatch(FinishedMatch match, long now);
    Task<Profile?> GetProfile(string accountId);
}

/// <summary>
/// One SQL implementation for SQLite and PostgreSQL. It uses only syntax both accept: TEXT and BIGINT columns,
/// named parameters, ON CONFLICT and LIMIT. Times are Unix seconds.
/// </summary>
public sealed class SqlAccountStore(Func<DbConnection> connect, bool sqlite) : IAccountStore
{
    private static readonly string[] Migrations =
    [
        """
        CREATE TABLE accounts (id TEXT PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL UNIQUE, password_hash TEXT NOT NULL, created_at BIGINT NOT NULL);
        CREATE TABLE sessions (token_hash TEXT PRIMARY KEY, account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, expires_at BIGINT NOT NULL);
        CREATE INDEX sessions_account ON sessions(account_id);
        CREATE TABLE matches (id TEXT PRIMARY KEY, mode BIGINT NOT NULL, outcome BIGINT NOT NULL, ticks BIGINT NOT NULL, finished_at BIGINT NOT NULL);
        CREATE TABLE match_players (match_id TEXT NOT NULL REFERENCES matches(id) ON DELETE CASCADE, account_id TEXT NOT NULL REFERENCES accounts(id) ON DELETE CASCADE, hero TEXT NOT NULL, PRIMARY KEY (match_id, account_id));
        CREATE INDEX match_players_account ON match_players(account_id);
        """
    ];
    public static int SchemaVersion => Migrations.Length;

    private async Task<DbConnection> Open()
    {
        var connection = connect();
        try
        {
            await connection.OpenAsync();
            if (sqlite) await Run(connection, null, "PRAGMA foreign_keys = ON;");
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static DbCommand Command(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object Value)[] values)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql; command.Transaction = transaction;
        foreach (var (name, value) in values)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter);
        }
        return command;
    }

    private static async Task<int> Run(DbConnection connection, DbTransaction? transaction, string sql, params (string Name, object Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync();
    }

    public async Task Migrate()
    {
        await using var connection = await Open();
        if (sqlite) await Run(connection, null, "PRAGMA journal_mode = WAL;");
        await Run(connection, null, "CREATE TABLE IF NOT EXISTS schema_version (version BIGINT NOT NULL);");
        long current;
        await using (var read = Command(connection, null, "SELECT COALESCE(MAX(version), 0) FROM schema_version;")) current = Convert.ToInt64(await read.ExecuteScalarAsync());
        if (current > Migrations.Length) throw new InvalidOperationException($"Database schema {current} is newer than this server supports ({Migrations.Length}).");
        for (var version = (int)current; version < Migrations.Length; version++)
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await Run(connection, transaction, Migrations[version]);
            await Run(connection, transaction, "INSERT INTO schema_version (version) VALUES (@version);", ("@version", (long)version + 1));
            await transaction.CommitAsync();
        }
    }

    public async Task<bool> CreateAccount(string id, string name, string nameKey, string passwordHash, long now)
    {
        await using var connection = await Open();
        return await Run(connection, null, "INSERT INTO accounts (id, name, name_key, password_hash, created_at) VALUES (@id, @name, @key, @hash, @now) ON CONFLICT (name_key) DO NOTHING;",
            ("@id", id), ("@name", name), ("@key", nameKey), ("@hash", passwordHash), ("@now", now)) == 1;
    }

    public async Task<(string Id, string Name, string PasswordHash)?> FindByName(string nameKey)
    {
        await using var connection = await Open();
        await using var command = Command(connection, null, "SELECT id, name, password_hash FROM accounts WHERE name_key = @key;", ("@key", nameKey));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetString(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task CreateSession(string tokenHash, string accountId, long expiresAt, int keep)
    {
        await using var connection = await Open();
        await using var transaction = await connection.BeginTransactionAsync();
        await Run(connection, transaction, "INSERT INTO sessions (token_hash, account_id, expires_at) VALUES (@token, @account, @expires);", ("@token", tokenHash), ("@account", accountId), ("@expires", expiresAt));
        await Run(connection, transaction, "DELETE FROM sessions WHERE account_id = @account AND token_hash NOT IN (SELECT token_hash FROM sessions WHERE account_id = @account ORDER BY expires_at DESC, token_hash LIMIT @keep);",
            ("@account", accountId), ("@keep", (long)keep));
        await transaction.CommitAsync();
    }

    public async Task<Account?> FindSession(string tokenHash, long now)
    {
        await using var connection = await Open();
        await using var command = Command(connection, null, "SELECT a.id, a.name FROM sessions s JOIN accounts a ON a.id = s.account_id WHERE s.token_hash = @token AND s.expires_at > @now;", ("@token", tokenHash), ("@now", now));
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? new Account(reader.GetString(0), reader.GetString(1)) : null;
    }

    public async Task DeleteSession(string tokenHash)
    {
        await using var connection = await Open();
        await Run(connection, null, "DELETE FROM sessions WHERE token_hash = @token;", ("@token", tokenHash));
    }

    public async Task<int> PurgeSessions(long now)
    {
        await using var connection = await Open();
        return await Run(connection, null, "DELETE FROM sessions WHERE expires_at <= @now;", ("@now", now));
    }

    public async Task RecordMatch(FinishedMatch match, long now)
    {
        if (match.Players.Length == 0) return;
        await using var connection = await Open();
        await using var transaction = await connection.BeginTransactionAsync();
        var inserted = await Run(connection, transaction, "INSERT INTO matches (id, mode, outcome, ticks, finished_at) VALUES (@id, @mode, @outcome, @ticks, @now) ON CONFLICT (id) DO NOTHING;",
            ("@id", match.Id), ("@mode", (long)match.Mode), ("@outcome", match.Won ? 1L : 0L), ("@ticks", match.Ticks), ("@now", now));
        if (inserted == 1)
            foreach (var player in match.Players)
                await Run(connection, transaction, "INSERT INTO match_players (match_id, account_id, hero) VALUES (@match, @account, @hero) ON CONFLICT (match_id, account_id) DO NOTHING;",
                    ("@match", match.Id), ("@account", player.Account), ("@hero", player.Hero));
        await transaction.CommitAsync();
    }

    public async Task<Profile?> GetProfile(string accountId)
    {
        await using var connection = await Open();
        string name; long created;
        await using (var command = Command(connection, null, "SELECT name, created_at FROM accounts WHERE id = @id;", ("@id", accountId)))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return null;
            name = reader.GetString(0); created = Convert.ToInt64(reader.GetValue(1));
        }
        var modes = new List<ModeRecord>();
        await using (var command = Command(connection, null, "SELECT m.mode, COUNT(*), SUM(m.outcome) FROM match_players p JOIN matches m ON m.id = p.match_id WHERE p.account_id = @id GROUP BY m.mode ORDER BY m.mode;", ("@id", accountId)))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) modes.Add(new((GameMode)Convert.ToInt32(reader.GetValue(0)), Convert.ToInt32(reader.GetValue(1)), Convert.ToInt32(reader.GetValue(2))));
        var recent = new List<ResultView>();
        await using (var command = Command(connection, null, "SELECT m.mode, p.hero, m.outcome, m.ticks, m.finished_at FROM match_players p JOIN matches m ON m.id = p.match_id WHERE p.account_id = @id ORDER BY m.finished_at DESC, m.id LIMIT 10;", ("@id", accountId)))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                recent.Add(new((GameMode)Convert.ToInt32(reader.GetValue(0)), reader.GetString(1), Convert.ToInt64(reader.GetValue(2)) == 1, Convert.ToInt64(reader.GetValue(3)) * BattleNusantara.Core.Match.StepSeconds, Convert.ToInt64(reader.GetValue(4))));
        return new(name, created, modes.Sum(m => m.Played), modes.Sum(m => m.Wins), modes.ToArray(), recent.ToArray());
    }
}

/// <summary>Registration, login and session checks. Session tokens are random; only their SHA-256 is stored.</summary>
public sealed partial class AccountService(IAccountStore store, TimeProvider time)
{
    public const int MinPassword = 8, MaxPassword = 128, MaxSessions = 5;
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    [GeneratedRegex("^[A-Za-z0-9_]{3,20}$")] private static partial Regex NamePattern();
    // Verified when the name is unknown, so a missing account costs the same time as a wrong password.
    private readonly Lazy<string> decoy = new(() => PasswordHasher.Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))));
    private long Now => time.GetUtcNow().ToUnixTimeSeconds();

    private static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<SessionGrant> Grant(string accountId, string name)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        await store.CreateSession(TokenHash(token), accountId, Now + (long)SessionLifetime.TotalSeconds, MaxSessions);
        return new(token, name);
    }

    /// <summary>Error is "name", "password" or "taken"; null on success.</summary>
    public async Task<(SessionGrant? Grant, string? Error)> Register(string? name, string? password)
    {
        if (name is null || !NamePattern().IsMatch(name)) return (null, "name");
        if (password is null || password.Length < MinPassword || password.Length > MaxPassword) return (null, "password");
        var id = Guid.NewGuid().ToString("N");
        if (!await store.CreateAccount(id, name, name.ToLowerInvariant(), PasswordHasher.Hash(password), Now)) return (null, "taken");
        return (await Grant(id, name), null);
    }

    public async Task<SessionGrant?> Login(string? name, string? password)
    {
        if (name is null || password is null || name.Length > 20 || password.Length > MaxPassword) return null;
        var found = await store.FindByName(name.ToLowerInvariant());
        if (!PasswordHasher.Verify(password, found?.PasswordHash ?? decoy.Value) || found is null) return null;
        return await Grant(found.Value.Id, found.Value.Name);
    }

    public async Task<Account?> Authenticate(string? token) =>
        string.IsNullOrEmpty(token) || token.Length > 128 ? null : await store.FindSession(TokenHash(token), Now);

    public Task Logout(string? token) => string.IsNullOrEmpty(token) || token.Length > 128 ? Task.CompletedTask : store.DeleteSession(TokenHash(token));
    public Task<Profile?> Profile(string accountId) => store.GetProfile(accountId);
    public Task<int> PurgeExpired() => store.PurgeSessions(Now);
}
