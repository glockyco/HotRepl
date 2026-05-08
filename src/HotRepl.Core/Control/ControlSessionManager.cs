using System;
using System.Collections.Generic;

namespace HotRepl.Control;

/// <summary>Tracks authenticated control sessions and the exclusive mutating-command lease.</summary>
internal sealed class ControlSessionManager
{
    private readonly ReplConfig _config;
    private readonly object _sync = new();
    private readonly Dictionary<string, ControlSession> _sessions = new(StringComparer.Ordinal);
    private ControlLease? _activeLease;

    public ControlSessionManager(ReplConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public ControlAuthResult Authenticate(Guid connectionId, string? token)
    {
        if (
            (_config.RequireControlAuth || !string.IsNullOrEmpty(_config.ControlAuthToken))
            && !string.Equals(token, _config.ControlAuthToken, StringComparison.Ordinal)
        )
        {
            return ControlAuthResult.Failed(
                new ControlCommandError(
                    "auth_failed",
                    "invalidToken",
                    "Control-plane authentication failed.",
                    Retryable: false
                )
            );
        }

        var sessionId = Guid.NewGuid().ToString("N");
        lock (_sync)
        {
            _sessions[sessionId] = new ControlSession(
                sessionId,
                connectionId,
                DateTimeOffset.UtcNow
            );
        }

        return ControlAuthResult.Succeeded(sessionId);
    }

    public ControlLeaseResult AcquireLease(string sessionId, string clientName)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return ControlLeaseResult.Failed(
                    new ControlCommandError(
                        "auth_failed",
                        "unknownSession",
                        "Control session is not authenticated.",
                        Retryable: true
                    )
                );
            }

            if (_activeLease != null)
            {
                return ControlLeaseResult.Failed(
                    new ControlCommandError(
                        "lease_conflict",
                        "leaseAlreadyHeld",
                        $"Control lease is already held by '{_activeLease.ClientName}'.",
                        Retryable: true
                    )
                );
            }

            var leaseId = Guid.NewGuid().ToString("N");
            _activeLease = new ControlLease(
                leaseId,
                session.SessionId,
                clientName,
                DateTimeOffset.UtcNow
            );
            return ControlLeaseResult.Succeeded(leaseId);
        }
    }

    public void OnDisconnected(Guid connectionId)
    {
        lock (_sync)
        {
            var removedSessionIds = new List<string>();
            foreach (var session in _sessions.Values)
            {
                if (session.ConnectionId == connectionId)
                    removedSessionIds.Add(session.SessionId);
            }

            foreach (var sessionId in removedSessionIds)
                _sessions.Remove(sessionId);

            if (_activeLease != null && removedSessionIds.Contains(_activeLease.SessionId))
                _activeLease = null;
        }
    }

    public bool IsLeaseValid(string? leaseId)
    {
        if (!_config.RequireControlLease)
            return true;
        if (string.IsNullOrEmpty(leaseId))
            return false;

        lock (_sync)
            return string.Equals(_activeLease?.LeaseId, leaseId, StringComparison.Ordinal);
    }

    private sealed record ControlSession(
        string SessionId,
        Guid ConnectionId,
        DateTimeOffset CreatedAt
    );

    private sealed record ControlLease(
        string LeaseId,
        string SessionId,
        string ClientName,
        DateTimeOffset CreatedAt
    );
}
