using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using AssettoServer.RaceControl.Core.Configuration;

namespace AssettoServer.RaceControl.Core.Web;

public enum WebRole
{
    /// <summary>Not signed in: can watch the live view and sign in.</summary>
    Visitor,
    /// <summary>Signed in with Steam: can register for the event.</summary>
    Player,
    /// <summary>The host PC, or a listed admin Steam ID: full controls.</summary>
    Admin,
}

public sealed record WebSession(string Id, string SteamId, string CsrfToken, DateTimeOffset ExpiresAt);

public sealed record WebAccess(WebRole Role, WebSession? Session)
{
    public string? SteamId => Session?.SteamId;
}

public static class WebAccessPolicy
{
    /// <summary>
    /// A browser on the host PC itself (loopback) is always admin, as before sign-in existed. Elsewhere a
    /// signed-in Steam ID listed in Settings is admin, any other signed-in player is a player, and
    /// everyone else is a visitor.
    /// </summary>
    public static WebRole Resolve(IPAddress? remoteAddress, string? steamId, IReadOnlyCollection<string> adminSteamIds)
    {
        if (remoteAddress is not null && IPAddress.IsLoopback(remoteAddress))
            return WebRole.Admin;
        if (string.IsNullOrEmpty(steamId))
            return WebRole.Visitor;
        return adminSteamIds.Contains(steamId) ? WebRole.Admin : WebRole.Player;
    }
}

/// <summary>
/// Signed-in browser sessions and pending Steam sign-ins, kept in memory: restarting the Web GUI signs
/// everyone out. Session ids and tokens are random 256-bit values.
/// </summary>
public sealed class WebSessionStore
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan SignInLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, WebSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _pendingSignIns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _usedNonces = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public WebSessionStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public WebSession Create(string steamId)
    {
        if (!SteamIds.IsValid(steamId))
            throw new ArgumentException("Not a SteamID64.", nameof(steamId));
        Prune();
        var session = new WebSession(NewToken(), steamId, NewToken(), _time.GetUtcNow() + SessionLifetime);
        _sessions[session.Id] = session;
        return session;
    }

    public WebSession? Find(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_sessions.TryGetValue(id, out var session))
            return null;
        if (session.ExpiresAt > _time.GetUtcNow())
            return session;
        _sessions.TryRemove(id, out _);
        return null;
    }

    public void Remove(string? id)
    {
        if (!string.IsNullOrEmpty(id))
            _sessions.TryRemove(id, out _);
    }

    /// <summary>A one-time value tying Steam's answer to the browser that started the sign-in.</summary>
    public string BeginSignIn()
    {
        Prune();
        string state = NewToken();
        _pendingSignIns[state] = _time.GetUtcNow() + SignInLifetime;
        return state;
    }

    public bool CompleteSignIn(string? state)
        => !string.IsNullOrEmpty(state)
           && _pendingSignIns.TryRemove(state, out var expiresAt)
           && expiresAt > _time.GetUtcNow();

    /// <summary>Rejects a replayed Steam answer: each response nonce is accepted once.</summary>
    public bool TryUseNonce(string? nonce)
        => !string.IsNullOrEmpty(nonce)
           && _usedNonces.TryAdd(nonce, _time.GetUtcNow() + TimeSpan.FromHours(1));

    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var session in _sessions.Where(pair => pair.Value.ExpiresAt <= now))
            _sessions.TryRemove(session.Key, out _);
        foreach (var pending in _pendingSignIns.Where(pair => pair.Value <= now))
            _pendingSignIns.TryRemove(pending.Key, out _);
        foreach (var nonce in _usedNonces.Where(pair => pair.Value <= now))
            _usedNonces.TryRemove(nonce.Key, out _);
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
