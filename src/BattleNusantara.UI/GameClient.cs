using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BattleNusantara.Core;
using Microsoft.AspNetCore.SignalR.Client;

namespace BattleNusantara.UI;

public sealed class GameClient : IAsyncDisposable
{
    /// <summary>Reconnect attempts after a dropped LAN connection, and the pause between them.</summary>
    public static int ReconnectAttempts { get; set; } = 10;
    public static TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>Longest a single reconnect attempt may take; an address that accepts the connection but never answers must not stall the client.</summary>
    public static TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>How long the client waits for an NPC before it shows the authored answer.</summary>
    public static TimeSpan NpcTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public const int MaxNpcQuestion = 160;

    /// <summary>
    /// Asks an NPC one of its authored topics. A server may answer with a generated reply; without a server, on any
    /// failure, or after <see cref="NpcTimeout"/>, the authored answer is returned, so this never fails for a known topic.
    /// The text is shown by the UI only and never enters the simulation.
    /// </summary>
    public async Task<NpcAnswer?> AskNpc(string? endpoint, string topicId, string language)
    {
        if (Story.Topic(topicId) is not { } topic) return null;
        var english = language == "en";
        var authored = new NpcAnswer(topic.Id, english ? topic.AnswerEn : topic.Answer, "authored");
        if (string.IsNullOrWhiteSpace(endpoint)) return authored;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            limit.CancelAfter(NpcTimeout);
            var reply = await http.GetFromJsonAsync<NpcAnswer>(new Uri(Address(endpoint), $"/api/npc/ask?topic={Uri.EscapeDataString(topic.Id)}&lang={(english ? "en" : "id")}"), limit.Token);
            return reply is { Text.Length: > 0 and <= 400, Source: "llm" or "authored" } && reply.Topic == topic.Id ? reply : authored;
        }
        catch (Exception) { return authored; }
    }

    /// <summary>
    /// Puts a typed question to an NPC. Offline, or when the server fails, the question is routed by keyword to an
    /// authored answer, or the NPC's authored "I don't know". Null for an unknown NPC or an empty or overlong question.
    /// </summary>
    public async Task<NpcAnswer?> TalkToNpc(string? endpoint, string npcId, string? question, string language)
    {
        if (Story.Npc(npcId) is not { } npc || string.IsNullOrWhiteSpace(question) || question.Trim().Length > MaxNpcQuestion) return null;
        var english = language == "en";
        var topic = Story.MatchTopic(npc.Id, question);
        var authored = topic is null ? new NpcAnswer("", english ? npc.UnknownEn : npc.Unknown, "authored") : new NpcAnswer(topic.Id, english ? topic.AnswerEn : topic.Answer, "authored");
        if (string.IsNullOrWhiteSpace(endpoint)) return authored;
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            limit.CancelAfter(NpcTimeout);
            using var response = await http.PostAsJsonAsync(new Uri(Address(endpoint), "/api/npc/talk"), new { npc = npc.Id, text = question.Trim(), lang = english ? "en" : "id" }, limit.Token);
            if (!response.IsSuccessStatusCode) return authored;
            var reply = await response.Content.ReadFromJsonAsync<NpcAnswer>(limit.Token);
            return reply is { Text.Length: > 0 and <= 400, Source: "llm" or "authored" } ? reply : authored;
        }
        catch (Exception) { return authored; }
    }
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task loop;
    private Match? local;
    private HubConnection? connection;
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private Uri? server, accountServer;
    private string? token, session;
    /// <summary>Signed-in account name, or null for a guest.</summary>
    public string? AccountName { get; private set; }
    public PlayerProfile? Profile { get; private set; }
    /// <summary>Waiting in the matchmaking queue. <see cref="Queue"/> says how many wait and how many a match needs.</summary>
    public bool Queued { get; private set; }
    public QueueStatus? Queue { get; private set; }
    /// <summary>Why the server closed the last queue ticket: "expired", "replaced" or "unavailable".</summary>
    public string? QueueClosed { get; private set; }
    /// <summary>The server address and session token to store for the next launch, or null when signed out.</summary>
    public (string Server, string Token)? Session => accountServer is not null && session is not null ? (accountServer.GetLeftPart(UriPartial.Authority), session) : null;
    public MatchView? View { get; private set; }
    public string? RoomCode { get; private set; }
    public int Owner { get; private set; }
    public bool Paused { get; set; }
    public bool Spectating { get; private set; }
    public bool Reconnecting { get; private set; }
    public string? NetworkError { get; private set; }
    public event Action? Changed;

    public GameClient() => loop = RunAsync();

    public async Task StartLocal(GameMode mode, string hero)
    {
        await Leave();
        await gate.WaitAsync();
        try { local = new Match(mode, hero, Random.Shared.Next()); Owner = 0; Paused = false; View = local.Snapshot(); }
        finally { gate.Release(); }
        Changed?.Invoke();
    }

    /// <summary>Resumes a solo match from <see cref="Save"/> output. Returns false if the data is not a valid save.</summary>
    public async Task<bool> StartSaved(string json)
    {
        var restored = Match.Load(json);
        if (restored is null) return false;
        await Leave();
        await gate.WaitAsync();
        try { local = restored; Owner = 0; Paused = false; View = local.Snapshot(); }
        finally { gate.Release(); }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Serialized solo match, or null when there is nothing that can be saved (LAN, other modes, finished).</summary>
    public async Task<string?> Save()
    {
        await gate.WaitAsync();
        try { return local?.Save(); }
        finally { gate.Release(); }
    }

    private static Uri Address(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https")
            ? uri : throw new ArgumentException("Use an http:// or https:// server address.");

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(accountServer!, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        return request;
    }

    /// <summary>
    /// Signs in or registers on a server. Returns null on success, otherwise "name", "password", "taken",
    /// "credentials", "limit" or "network".
    /// </summary>
    public async Task<string?> SignIn(string endpoint, string name, string password, bool register)
    {
        var address = Address(endpoint);
        try
        {
            using var response = await http.PostAsJsonAsync(new Uri(address, register ? "/api/account/register" : "/api/account/login"), new { name, password }, cancellation.Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return "limit";
            if (response.StatusCode == HttpStatusCode.Unauthorized) return "credentials";
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
            {
                var code = (await response.Content.ReadFromJsonAsync<AccountError>(cancellation.Token))?.Error;
                return code is "name" or "password" or "taken" ? code : "network";
            }
            if (!response.IsSuccessStatusCode) return "network";
            var grant = await response.Content.ReadFromJsonAsync<AccountGrant>(cancellation.Token);
            if (string.IsNullOrEmpty(grant?.Token)) return "network";
            await Leave();
            accountServer = address; session = grant.Token; AccountName = grant.Name; Profile = null;
            await RefreshProfile();
            Changed?.Invoke();
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return "network"; }
    }

    /// <summary>Restores a stored session. False when the server no longer accepts it.</summary>
    public async Task<bool> Resume(string endpoint, string storedToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) || string.IsNullOrEmpty(storedToken) || storedToken.Length > 128) return false;
        accountServer = address; session = storedToken;
        if (await RefreshProfile()) { AccountName = Profile!.Name; Changed?.Invoke(); return true; }
        accountServer = null; session = null;
        return false;
    }

    public async Task<bool> RefreshProfile()
    {
        if (session is null) return false;
        try
        {
            using var response = await http.SendAsync(Request(HttpMethod.Get, "/api/account/profile"), cancellation.Token);
            if (!response.IsSuccessStatusCode) return false;
            Profile = await response.Content.ReadFromJsonAsync<PlayerProfile>(cancellation.Token);
            return Profile is not null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { return false; }
    }

    public async Task SignOut()
    {
        if (session is null) return;
        await Leave();
        try { using var response = await http.SendAsync(Request(HttpMethod.Post, "/api/account/logout"), cancellation.Token); }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { } // The local session is dropped either way.
        accountServer = null; session = null; AccountName = null; Profile = null;
        Changed?.Invoke();
    }

    /// <summary>Joins the matchmaking queue of the server the account belongs to. A "Matched" message seats the player.</summary>
    public async Task FindMatch(GameMode mode, string hero)
    {
        if (session is null || accountServer is null) throw new InvalidOperationException("Sign in first.");
        await Leave();
        server = new Uri(accountServer, "/hubs/battle");
        var hub = Build();
        try
        {
            await hub.StartAsync(cancellation.Token);
            connection = hub;
            var status = await hub.InvokeAsync<QueueStatus>("Queue", mode.ToString(), hero, cancellation.Token);
            if (RoomCode is null) { Queued = true; Queue = status; }
            Changed?.Invoke();
        }
        catch { connection = null; await hub.DisposeAsync(); await Leave(); throw; }
    }

    public async Task CancelQueue()
    {
        if (connection is { State: HubConnectionState.Connected } hub && Queued)
        {
            try { await hub.InvokeAsync("CancelQueue", cancellation.Token); }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or Microsoft.AspNetCore.SignalR.HubException or IOException) { }
        }
        await Leave();
        Changed?.Invoke();
    }

    public async Task Connect(string endpoint, string? code, GameMode mode, string hero, bool watch = false)
    {
        var uri = Address(endpoint);
        var room = code?.Trim().ToUpperInvariant();
        if (watch && string.IsNullOrEmpty(room)) throw new ArgumentException("A room code is required to watch.");
        await Leave();
        server = new Uri(uri, "/hubs/battle");
        var hub = Build();
        try
        {
            await hub.StartAsync(cancellation.Token);
            var joined = watch
                ? await hub.InvokeAsync<RoomJoined>("Watch", room, cancellation.Token)
                : await hub.InvokeAsync<RoomJoined>(string.IsNullOrEmpty(room) ? "Create" : "Join", string.IsNullOrEmpty(room) ? mode.ToString() : room, hero, cancellation.Token);
            connection = hub;
            Apply(joined);
            Changed?.Invoke();
        }
        catch { await hub.DisposeAsync(); await Leave(); throw; }
    }

    private void Apply(RoomJoined joined)
    {
        RoomCode = joined.Code; Owner = joined.Owner; View = joined.State; token = joined.Token; Spectating = joined.Spectator;
    }

    private HubConnection Build()
    {
        // The session token is sent only to the server that issued it, never to another address the player types in.
        var credential = session is not null && accountServer is not null && new Uri(accountServer, "/hubs/battle") == server ? session : null;
        var hub = new HubConnectionBuilder().WithUrl(server!, options => { if (credential is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(credential); }).Build();
        hub.On<MatchView>("State", state => { if (ReferenceEquals(connection, hub)) { View = state; Changed?.Invoke(); } });
        hub.On<RoomJoined>("Matched", joined => { if (ReferenceEquals(connection, hub)) { Apply(joined); Queued = false; Queue = null; Changed?.Invoke(); } });
        hub.On<string>("QueueClosed", reason => { if (ReferenceEquals(connection, hub) && Queued) { Queued = false; Queue = null; QueueClosed = reason; Changed?.Invoke(); } });
        // Only the live connection reacts; a connection we replaced or left on purpose is ignored.
        hub.Closed += error => { if (ReferenceEquals(connection, hub)) { var reconnect = ReconnectAsync(hub); } return Task.CompletedTask; };
        return hub;
    }

    private async Task ReconnectAsync(HubConnection dropped)
    {
        var room = RoomCode; var seat = token; var watching = Spectating;
        if (room is null)
        {
            // Not seated: a lost queue ticket cannot be resumed, and an idle connection is simply dropped.
            var waiting = Queued;
            connection = null; Queued = false; Queue = null;
            if (waiting) NetworkError = "Koneksi terputus saat mencari pertandingan. / Disconnected while finding a match.";
            await dropped.DisposeAsync();
            Changed?.Invoke();
            return;
        }
        Reconnecting = true; Changed?.Invoke();
        for (var attempt = 0; attempt < ReconnectAttempts && ReferenceEquals(connection, dropped) && !cancellation.IsCancellationRequested; attempt++)
        {
            var hub = Build();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            try
            {
                if (attempt > 0) await Task.Delay(ReconnectDelay, cancellation.Token);
                limit.CancelAfter(ReconnectTimeout);
                await hub.StartAsync(limit.Token);
                var joined = watching
                    ? await hub.InvokeAsync<RoomJoined>("Watch", room, limit.Token)
                    : await hub.InvokeAsync<RoomJoined>("Rejoin", room, seat, limit.Token);
                if (!ReferenceEquals(connection, dropped)) { await hub.DisposeAsync(); return; } // Left meanwhile.
                connection = hub;
                Apply(joined);
                Reconnecting = false; Changed?.Invoke();
                await dropped.DisposeAsync();
                return;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { await hub.DisposeAsync(); } // This attempt timed out.
            catch (OperationCanceledException) { await hub.DisposeAsync(); return; }
            // Any other failure is a failed attempt. Listing exception types here once left the client stuck on
            // "Reconnecting" when the address answered with something that was not this server.
            catch (Exception exception)
            {
                await hub.DisposeAsync();
                // The server answered but the seat or room is gone: retrying cannot help.
                if (exception is Microsoft.AspNetCore.SignalR.HubException) break;
            }
        }
        if (!ReferenceEquals(connection, dropped)) return;
        Reconnecting = false;
        NetworkError = "Koneksi terputus. Kembali ke menu dan gabung ulang. / Disconnected. Return to menu and rejoin.";
        Changed?.Invoke();
    }

    /// <summary>Test hook: closes the transport as a network failure would, without leaving the room.</summary>
    internal Task DropConnectionForTest() => connection?.StopAsync() ?? Task.CompletedTask;

    public async Task<bool> Command(GameCommand command)
    {
        if (Paused || Spectating || Reconnecting || NetworkError is not null || View?.Status != MatchStatus.Playing) return false;
        if (connection is not null) return await connection.InvokeAsync<bool>("Command", command, cancellation.Token);
        await gate.WaitAsync();
        try { var accepted = local?.Execute(Owner, command) ?? false; View = local?.Snapshot(Owner); return accepted; }
        finally { gate.Release(); }
    }

    public async Task Leave()
    {
        var hub = connection; connection = null; // Clearing first marks the close as deliberate.
        if (hub is not null)
        {
            try
            {
                if (hub.State == HubConnectionState.Connected)
                {
                    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await hub.InvokeAsync("Leave", limit.Token);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or Microsoft.AspNetCore.SignalR.HubException or IOException) { }
            await hub.DisposeAsync();
        }
        await gate.WaitAsync();
        try { local = null; View = null; RoomCode = null; NetworkError = null; Paused = false; token = null; Spectating = false; Reconnecting = false; Queued = false; Queue = null; QueueClosed = null; }
        finally { gate.Release(); }
    }

    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Match.StepSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token))
            {
                var changed = false;
                await gate.WaitAsync(cancellation.Token);
                try
                {
                    if (local is not null && !Paused && local.Status == MatchStatus.Playing)
                    { local.Advance(); View = local.Snapshot(Owner); changed = true; }
                }
                finally { gate.Release(); }
                if (changed) Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await cancellation.CancelAsync();
        await loop;
        await Leave();
        cancellation.Dispose(); gate.Dispose();
    }
}
public sealed record RoomJoined(string Code, int Owner, MatchView State, string Token = "", bool Spectator = false);
/// <summary>Matchmaking: players waiting for the mode, and how many a match needs. Waiting is 0 once matched.</summary>
/// <summary>The Keeper's reply; Source is "llm" (generated on the server) or "authored".</summary>
public sealed record NpcAnswer(string Topic, string Text, string Source);
public sealed record QueueStatus(int Waiting, int Needed);
public sealed record ModeStat(GameMode Mode, int Played, int Wins);
public sealed record MatchSummary(GameMode Mode, string Hero, bool Won, double Seconds, long FinishedAt);
public sealed record PlayerProfile(string Name, long CreatedAt, int Played, int Wins, ModeStat[] Modes, MatchSummary[] Recent);
internal sealed record AccountGrant(string Token, string Name);
internal sealed record AccountError(string? Error);
