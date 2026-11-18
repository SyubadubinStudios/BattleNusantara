using System.Security.Cryptography;
using System.Security.Claims;
using BattleNusantara.Core;
using BattleNusantara.Server.Platform;
using BattleNusantara.UI;
using Microsoft.AspNetCore.SignalR;

namespace BattleNusantara.Server;

/// <summary>
/// In-process rooms. A player slot outlives its connection: a dropped player may rejoin with the slot's token
/// within <see cref="GraceTicks"/>; after that, or on an explicit leave, the player is removed from the match.
/// </summary>
public sealed class RoomRegistry
{
    public const int MaxRooms = 32, MaxPlayers = Match.MaxPlayers, MaxSpectators = 8;
    /// <summary>Loop ticks (10 per second) a disconnected slot, or a room nobody is connected to, is kept.</summary>
    public const int GraceTicks = 600;
    private readonly Dictionary<string, Room> rooms = new();
    private readonly Dictionary<string, string> membership = new();
    private readonly List<FinishedMatch> finished = new();
    private readonly object gate = new();

    public int RoomCount { get { lock (gate) return rooms.Count; } }
    public bool InRoom(string connection) { lock (gate) return membership.ContainsKey(connection); }
    public (int Rooms, int Players, int Spectators) Census()
    {
        lock (gate) return (rooms.Count, rooms.Values.Sum(r => r.Slots.Count), rooms.Values.Sum(r => r.Spectators.Count));
    }
    /// <summary>Matches that ended since the last call, for the result store. Each room is reported once.</summary>
    public FinishedMatch[] TakeFinished()
    {
        lock (gate) { var taken = finished.ToArray(); finished.Clear(); return taken; }
    }

    /// <param name="account">The signed-in account, or null for a guest. Guests are never written to match results.</param>
    /// <param name="map">For a new room, a map id of its mode; null or empty plays the mode's default map.</param>
    public RoomJoined Enter(string connection, string modeOrCode, string hero, bool create, string? account = null, string? map = null)
    {
        lock (gate)
        {
            if (membership.ContainsKey(connection)) throw new HubException("Leave the current room first.");
            if (!Catalog.Heroes.Any(h => h.Id == hero)) throw new HubException("Unknown hero.");
            Room room; int owner;
            if (create)
            {
                if (rooms.Count >= MaxRooms) throw new HubException("Server is full. Try again later.");
                if (!Enum.TryParse<GameMode>(modeOrCode, out var mode) || !Enum.IsDefined(mode)) throw new HubException("Unknown mode.");
                var definition = Maps.Resolve(mode, map) ?? throw new HubException("Unknown map.");
                string code;
                do { code = Convert.ToHexString(RandomNumberGenerator.GetBytes(3)); } while (rooms.ContainsKey(code));
                room = new Room(code, new Match(mode, hero, Random.Shared.Next(), definition.Id));
                rooms.Add(code, room);
                owner = 0;
            }
            else
            {
                if (!rooms.TryGetValue(modeOrCode, out room!)) throw new HubException("Room not found.");
                if (room.Slots.Count >= Match.Capacity(room.Match.Mode) || room.Match.Status != MatchStatus.Playing) throw new HubException("Room is full or finished. Create a new room.");
                // A slot freed by a departed player is reused.
                owner = Enumerable.Range(0, MaxPlayers).First(id => !room.Slots.ContainsKey(id));
                room.Match.AddPlayer(owner, hero);
            }
            var slot = new Slot(Convert.ToHexString(RandomNumberGenerator.GetBytes(16))) { Connection = connection, Account = account, Hero = hero };
            room.Slots.Add(owner, slot);
            membership.Add(connection, room.Code);
            return new RoomJoined(room.Code, owner, room.Match.Snapshot(owner), slot.Token);
        }
    }

    /// <summary>Reattaches a new connection to the slot that owns <paramref name="token"/>.</summary>
    public RoomJoined Rejoin(string connection, string code, string token)
    {
        lock (gate)
        {
            if (membership.ContainsKey(connection)) throw new HubException("Leave the current room first.");
            if (string.IsNullOrEmpty(token) || !rooms.TryGetValue(code, out var room)) throw new HubException("Room not found.");
            var (owner, slot) = room.Slots.FirstOrDefault(s => CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(s.Value.Token), System.Text.Encoding.UTF8.GetBytes(token)));
            if (slot is null) throw new HubException("Seat expired. Join again.");
            // The old connection may not have been reported closed yet; the token holder takes the seat.
            if (slot.Connection is not null) { membership.Remove(slot.Connection); room.Rates.Remove(slot.Connection); }
            slot.Connection = connection; slot.DisconnectedAt = null;
            membership.Add(connection, room.Code);
            return new RoomJoined(room.Code, owner, room.Match.Snapshot(owner), slot.Token);
        }
    }

    /// <summary>Joins as a read-only spectator. Spectators see what the allied team sees.</summary>
    public RoomJoined Watch(string connection, string code)
    {
        lock (gate)
        {
            if (membership.ContainsKey(connection)) throw new HubException("Leave the current room first.");
            if (!rooms.TryGetValue(code, out var room)) throw new HubException("Room not found.");
            if (room.Spectators.Count >= MaxSpectators) throw new HubException("Spectator seats are full.");
            room.Spectators.Add(connection);
            membership.Add(connection, room.Code);
            return new RoomJoined(room.Code, -1, room.Match.Snapshot(-1), "", true);
        }
    }

    public bool Command(string connection, GameCommand command)
    {
        lock (gate)
        {
            if (!membership.TryGetValue(connection, out var code)) return false;
            var room = rooms[code];
            var owner = room.OwnerOf(connection);
            if (owner < 0) return false; // Spectators cannot command.
            // A connection may send at most 20 commands per simulation second.
            var window = room.Match.Tick / 10;
            var rate = room.Rates.GetValueOrDefault(connection);
            var count = rate.Window == window ? rate.Count + 1 : 1;
            room.Rates[connection] = (window, count);
            return count <= 20 && room.Match.Execute(owner, command);
        }
    }

    /// <summary>Deliberate departure: the player's seat is freed at once.</summary>
    public void Leave(string connection)
    {
        lock (gate)
        {
            if (!membership.Remove(connection, out var code)) return;
            var room = rooms[code];
            room.Rates.Remove(connection);
            if (room.Spectators.Remove(connection)) return;
            var owner = room.OwnerOf(connection);
            if (owner >= 0) Vacate(room, owner);
            if (room.Slots.Count == 0) Close(room);
        }
    }

    /// <summary>Lost connection: the seat is held for <see cref="GraceTicks"/> so the player can rejoin.</summary>
    public void Disconnect(string connection)
    {
        lock (gate)
        {
            if (!membership.Remove(connection, out var code)) return;
            var room = rooms[code];
            room.Rates.Remove(connection);
            if (room.Spectators.Remove(connection)) return;
            var owner = room.OwnerOf(connection);
            if (owner < 0) return;
            room.Slots[owner].Connection = null; room.Slots[owner].DisconnectedAt = room.Age;
        }
    }

    public List<(string Connection, MatchView State)> Advance()
    {
        lock (gate)
        {
            var states = new List<(string, MatchView)>();
            foreach (var room in rooms.Values.ToArray())
            {
                room.Age++;
                foreach (var (owner, slot) in room.Slots.ToArray())
                    if (slot.DisconnectedAt is { } since && room.Age - since >= GraceTicks) Vacate(room, owner);
                room.Idle = room.Slots.Values.Any(s => s.Connection is not null) ? 0 : room.Idle + 1;
                if (room.Slots.Count == 0 || room.Idle >= GraceTicks) { Close(room); continue; }
                room.Match.Advance();
                if (room.Match.Status != MatchStatus.Playing && !room.Recorded)
                {
                    room.Recorded = true;
                    var seats = room.Slots.Values.Where(s => s.Account is not null).Select(s => new SeatResult(s.Account!, s.Hero)).DistinctBy(s => s.Account).ToArray();
                    if (seats.Length > 0) finished.Add(new(room.Id, room.Match.Mode, room.Match.Status == MatchStatus.Victory, room.Match.Tick, seats));
                }
                foreach (var (owner, slot) in room.Slots)
                    if (slot.Connection is not null) states.Add((slot.Connection, room.Match.Snapshot(owner)));
                if (room.Spectators.Count > 0)
                {
                    var view = room.Match.Snapshot(-1);
                    foreach (var spectator in room.Spectators) states.Add((spectator, view));
                }
            }
            return states;
        }
    }

    private static void Vacate(Room room, int owner)
    {
        room.Slots.Remove(owner);
        room.Match.RemovePlayer(owner);
    }

    private void Close(Room room)
    {
        rooms.Remove(room.Code);
        foreach (var slot in room.Slots.Values) if (slot.Connection is not null) membership.Remove(slot.Connection);
        foreach (var spectator in room.Spectators) membership.Remove(spectator);
    }

    private sealed class Slot(string token)
    {
        public string Token { get; } = token;
        public string? Connection { get; set; }
        public string? Account { get; init; }
        public string Hero { get; init; } = "";
        public long? DisconnectedAt { get; set; }
    }

    private sealed class Room(string code, Match match)
    {
        public string Code { get; } = code;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public bool Recorded { get; set; }
        public Match Match { get; } = match;
        public long Age { get; set; }
        public int Idle { get; set; }
        public Dictionary<int, Slot> Slots { get; } = new();
        public HashSet<string> Spectators { get; } = new();
        public Dictionary<string, (long Window, int Count)> Rates { get; } = new();
        public int OwnerOf(string connection)
        {
            foreach (var (owner, slot) in Slots) if (slot.Connection == connection) return owner;
            return -1;
        }
    }
}

/// <summary>
/// The game hub. Guests may create, join and watch rooms unless Platform:RequireAccount is set; matchmaking always
/// needs a signed-in account. The account comes from the authenticated connection, never from a client argument.
/// </summary>
public sealed class BattleHub(RoomRegistry rooms, IServiceProvider services) : Hub
{
    private string? Account => Context.User?.Identity?.IsAuthenticated == true ? Context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null;
    private PlatformOptions Options => services.GetService<PlatformOptions>() ?? new();
    private ILobbyGrain? Lobby => services.GetService<IGrainFactory>()?.GetGrain<ILobbyGrain>(0);
    private void RequireSeatAccess() { if (Options.RequireAccount && Account is null) throw new HubException("Sign in first."); }

    public RoomJoined Create(string mode, string hero, string? map) { RequireSeatAccess(); return rooms.Enter(Context.ConnectionId, mode, hero, true, Account, map); }
    public RoomJoined Join(string code, string hero) { RequireSeatAccess(); return rooms.Enter(Context.ConnectionId, code, hero, false, Account); }
    public RoomJoined Rejoin(string code, string token) => rooms.Rejoin(Context.ConnectionId, code, token);
    public RoomJoined Watch(string code) { RequireSeatAccess(); return rooms.Watch(Context.ConnectionId, code); }
    public void Leave() => rooms.Leave(Context.ConnectionId);
    public bool Command(GameCommand command) => rooms.Command(Context.ConnectionId, command);

    /// <summary>Waits for other players. When enough are waiting, each receives a "Matched" message with their seat.</summary>
    public async Task<QueueStatus> Queue(string mode, string hero)
    {
        if (Account is not { } account) throw new HubException("Sign in to use matchmaking.");
        if (Lobby is not { } lobby) throw new HubException("Matchmaking is not enabled on this server.");
        if (!Enum.TryParse<GameMode>(mode, out var parsed) || !Enum.IsDefined(parsed)) throw new HubException("Unknown mode.");
        if (!Catalog.Heroes.Any(h => h.Id == hero)) throw new HubException("Unknown hero.");
        if (rooms.InRoom(Context.ConnectionId)) throw new HubException("Leave the current room first.");
        var status = await lobby.Enqueue(new QueueTicket(Context.ConnectionId, account, (int)parsed, hero, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        return new QueueStatus(status[0], status[1]);
    }
    public Task CancelQueue() => Lobby?.Cancel(Context.ConnectionId) ?? Task.CompletedTask;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Leave the queue before releasing the seat, so a match formed meanwhile is still cleaned up by Disconnect.
        try { if (Lobby is { } lobby) await lobby.Cancel(Context.ConnectionId); }
        catch (Exception error) when (error is not OperationCanceledException) { services.GetService<ILogger<BattleHub>>()?.LogWarning(error, "Queue cleanup failed"); }
        rooms.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

public sealed class BattleLoop(RoomRegistry rooms, IHubContext<BattleHub> hub, ILogger<BattleLoop> logger, IServiceProvider services) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var metrics = services.GetService<LoopMetrics>();
        var store = services.GetService<IAccountStore>();
        var watch = new System.Diagnostics.Stopwatch();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Match.StepSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                watch.Restart();
                var sends = rooms.Advance().Select(s => hub.Clients.Client(s.Connection).SendAsync("State", s.State, stoppingToken));
                try { await Task.WhenAll(sends); }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { logger.LogWarning(exception, "Snapshot delivery failed"); }
                metrics?.Record(watch.Elapsed);
                if (store is null) continue;
                foreach (var match in rooms.TakeFinished())
                {
                    try { await store.RecordMatch(match, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); }
                    catch (System.Data.Common.DbException exception) { logger.LogError(exception, "Match {Match} could not be recorded", match.Id); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
