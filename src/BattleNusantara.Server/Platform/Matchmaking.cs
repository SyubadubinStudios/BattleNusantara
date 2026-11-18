using System.Text.Json;
using BattleNusantara.Core;
using BattleNusantara.UI;
using Microsoft.AspNetCore.SignalR;
using StackExchange.Redis;

namespace BattleNusantara.Server.Platform;

[GenerateSerializer]
public sealed record QueueTicket([property: Id(0)] string Connection, [property: Id(1)] string Account, [property: Id(2)] int Mode, [property: Id(3)] string Hero, [property: Id(4)] long At);

/// <summary>What the client is told about a waiting ticket or a closed one.</summary>
public sealed record Notice(string Connection, string Event, object Payload);

/// <summary>Waiting matchmaking tickets. One ticket per connection and per account.</summary>
public interface IQueueStore
{
    /// <summary>Adds the ticket. Returns the mode's queue length and the connection of a ticket it replaced for the same account, if any.</summary>
    Task<(int Waiting, string? Replaced)> Enqueue(QueueTicket ticket);
    Task<bool> Remove(string connection);
    /// <summary>Removes and returns the oldest <paramref name="size"/> tickets of a mode, or nothing when fewer wait.</summary>
    Task<QueueTicket[]> Take(int mode, int size);
    /// <summary>Removes and returns tickets enqueued before <paramref name="before"/> (Unix seconds).</summary>
    Task<QueueTicket[]> Expire(long before);
    Task<int> Count();
}

public sealed class InMemoryQueueStore : IQueueStore
{
    private readonly List<QueueTicket> tickets = new();
    private readonly object gate = new();

    public Task<(int Waiting, string? Replaced)> Enqueue(QueueTicket ticket)
    {
        lock (gate)
        {
            var old = tickets.Find(t => t.Account == ticket.Account && t.Connection != ticket.Connection);
            tickets.RemoveAll(t => t.Account == ticket.Account || t.Connection == ticket.Connection);
            tickets.Add(ticket);
            return Task.FromResult((tickets.Count(t => t.Mode == ticket.Mode), old?.Connection));
        }
    }

    public Task<bool> Remove(string connection) { lock (gate) return Task.FromResult(tickets.RemoveAll(t => t.Connection == connection) > 0); }

    public Task<QueueTicket[]> Take(int mode, int size)
    {
        lock (gate)
        {
            var group = tickets.Where(t => t.Mode == mode).Take(size).ToArray();
            if (group.Length < size) return Task.FromResult(Array.Empty<QueueTicket>());
            tickets.RemoveAll(group.Contains);
            return Task.FromResult(group);
        }
    }

    public Task<QueueTicket[]> Expire(long before)
    {
        lock (gate)
        {
            var expired = tickets.Where(t => t.At < before).ToArray();
            tickets.RemoveAll(expired.Contains);
            return Task.FromResult(expired);
        }
    }

    public Task<int> Count() { lock (gate) return Task.FromResult(tickets.Count); }
}

/// <summary>
/// Redis-backed queue for multi-host deployments. Tested against Garnet (Redis protocol) by RedisQueueTests when
/// NUSANTARA_TEST_REDIS is set; not run against Redis itself. Tickets carry a TTL so a crashed host cannot leave them
/// behind forever. The key prefix lets tests share one server.
/// </summary>
public sealed class RedisQueueStore(IConnectionMultiplexer redis, string prefix = "nusantara") : IQueueStore
{
    private static readonly TimeSpan Presence = TimeSpan.FromMinutes(5);
    private static readonly int[] Modes = Enum.GetValues<GameMode>().Select(m => (int)m).ToArray();
    private const string TakeScript = "if redis.call('LLEN', KEYS[1]) < tonumber(ARGV[1]) then return {} end return redis.call('LPOP', KEYS[1], ARGV[1])";
    private IDatabase Db => redis.GetDatabase();
    private RedisKey Queue(int mode) => $"{prefix}:queue:{mode}";
    private RedisKey Ticket(string connection) => $"{prefix}:ticket:{connection}";
    private RedisKey Owner(string account) => $"{prefix}:account:{account}";

    private async Task<QueueTicket?> Read(string connection)
    {
        var value = await Db.StringGetAsync(Ticket(connection));
        return value.IsNullOrEmpty ? null : JsonSerializer.Deserialize<QueueTicket>(value.ToString());
    }

    public async Task<(int Waiting, string? Replaced)> Enqueue(QueueTicket ticket)
    {
        var previous = await Db.StringGetAsync(Owner(ticket.Account));
        string? replaced = previous.IsNullOrEmpty || previous.ToString() == ticket.Connection ? null : previous.ToString();
        if (replaced is not null) await Remove(replaced);
        await Remove(ticket.Connection);
        await Db.StringSetAsync(Ticket(ticket.Connection), JsonSerializer.Serialize(ticket), Presence);
        await Db.StringSetAsync(Owner(ticket.Account), ticket.Connection, Presence);
        return ((int)await Db.ListRightPushAsync(Queue(ticket.Mode), ticket.Connection), replaced);
    }

    public async Task<bool> Remove(string connection)
    {
        var ticket = await Read(connection);
        if (ticket is null) return false;
        await Db.ListRemoveAsync(Queue(ticket.Mode), connection);
        await Db.KeyDeleteAsync([Ticket(connection), Owner(ticket.Account)]);
        return true;
    }

    public async Task<QueueTicket[]> Take(int mode, int size)
    {
        var popped = (RedisValue[]?)await Db.ScriptEvaluateAsync(TakeScript, [Queue(mode)], [size]) ?? [];
        var group = new List<QueueTicket>();
        foreach (var connection in popped)
        {
            // A ticket whose presence expired is dropped; the caller re-queues a short group.
            if (await Read(connection.ToString()) is not { } ticket) continue;
            await Db.KeyDeleteAsync([Ticket(ticket.Connection), Owner(ticket.Account)]);
            group.Add(ticket);
        }
        return group.ToArray();
    }

    public async Task<QueueTicket[]> Expire(long before)
    {
        var expired = new List<QueueTicket>();
        foreach (var mode in Modes)
            foreach (var connection in await Db.ListRangeAsync(Queue(mode)))
            {
                var ticket = await Read(connection.ToString());
                if (ticket is null) { await Db.ListRemoveAsync(Queue(mode), connection); continue; }
                if (ticket.At < before && await Remove(ticket.Connection)) expired.Add(ticket);
            }
        return expired.ToArray();
    }

    public async Task<int> Count()
    {
        long total = 0;
        foreach (var mode in Modes) total += await Db.ListLengthAsync(Queue(mode));
        return (int)total;
    }
}

/// <summary>Matchmaking rules, free of transport: it returns the notices to deliver instead of sending them.</summary>
public sealed class Matchmaker(IQueueStore queue, RoomRegistry rooms, PlatformOptions options)
{
    public const string Matched = "Matched", Closed = "QueueClosed";

    public static int PartySize(PlatformOptions options, GameMode mode) => Math.Clamp(options.PartySize, 2, Match.Capacity(mode));

    public async Task<(QueueStatus Status, List<Notice> Notices)> Enqueue(QueueTicket ticket)
    {
        var mode = (GameMode)ticket.Mode;
        if (!Enum.IsDefined(mode) || !Catalog.Heroes.Any(h => h.Id == ticket.Hero)) throw new ArgumentException("Unknown mode or hero.");
        var notices = new List<Notice>();
        var size = PartySize(options, mode);
        if (rooms.InRoom(ticket.Connection)) return (new(0, size), notices);
        var (waiting, replaced) = await queue.Enqueue(ticket);
        if (replaced is not null) notices.Add(new(replaced, Closed, "replaced"));
        var group = await queue.Take(ticket.Mode, size);
        if (group.Length == 0) return (new(waiting, size), notices);

        var seated = new List<(QueueTicket Ticket, RoomJoined Joined)>();
        string? code = null;
        // Matchmade rooms rotate through the mode's maps.
        var maps = Maps.For(mode).ToArray();
        var map = maps[Random.Shared.Next(maps.Length)].Id;
        foreach (var member in group)
        {
            try
            {
                var joined = rooms.Enter(member.Connection, code ?? mode.ToString(), member.Hero, code is null, member.Account, code is null ? map : null);
                code = joined.Code; seated.Add((member, joined));
            }
            catch (HubException) { notices.Add(new(member.Connection, Closed, "unavailable")); }
        }
        if (seated.Count == 1)
        {
            // Everyone else vanished: undo the room and keep the survivor waiting.
            rooms.Leave(seated[0].Ticket.Connection);
            var (again, _) = await queue.Enqueue(seated[0].Ticket);
            return (new(ticket.Connection == seated[0].Ticket.Connection ? again : 0, size), notices);
        }
        foreach (var (member, joined) in seated) notices.Add(new(member.Connection, Matched, joined));
        return (new(0, size), notices);
    }

    public Task<bool> Cancel(string connection) => queue.Remove(connection);

    public async Task<List<Notice>> Expire(long now)
    {
        var expired = await queue.Expire(now - options.TicketSeconds);
        return expired.Select(t => new Notice(t.Connection, Closed, "expired")).ToList();
    }
}

public interface ILobbyGrain : IGrainWithIntegerKey
{
    /// <summary>Returns { waiting, needed }.</summary>
    Task<int[]> Enqueue(QueueTicket ticket);
    Task Cancel(string connection);
}

/// <summary>
/// The single lobby activation serializes every matchmaking decision, so two players can never be matched twice,
/// and it is the only place that creates rooms on behalf of the queue.
/// </summary>
public sealed class LobbyGrain(Matchmaker matchmaker, IHubContext<BattleHub> hub, TimeProvider time, ILogger<LobbyGrain> logger) : Grain, ILobbyGrain
{
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        this.RegisterGrainTimer(static (self, _) => self.Sweep(), this, new GrainTimerCreationOptions { DueTime = TimeSpan.FromSeconds(5), Period = TimeSpan.FromSeconds(5), KeepAlive = true });
        return Task.CompletedTask;
    }

    public async Task<int[]> Enqueue(QueueTicket ticket)
    {
        var (status, notices) = await matchmaker.Enqueue(ticket);
        await Deliver(notices);
        return [status.Waiting, status.Needed];
    }

    public Task Cancel(string connection) => matchmaker.Cancel(connection);

    private async Task Sweep() => await Deliver(await matchmaker.Expire(time.GetUtcNow().ToUnixTimeSeconds()));

    private async Task Deliver(List<Notice> notices)
    {
        foreach (var notice in notices)
        {
            try { await hub.Clients.Client(notice.Connection).SendAsync(notice.Event, notice.Payload); }
            catch (Exception exception) when (exception is not OperationCanceledException) { logger.LogWarning(exception, "Matchmaking notice {Event} was not delivered", notice.Event); }
        }
    }
}
