using System;
using System.Collections.Generic;

namespace HotRepl.Control;

/// <summary>Tracks authenticated control sessions and the exclusive mutating-command lease.</summary>
internal sealed class ControlSessionManager
{
    private readonly ReplConfig _config;
    private readonly object _sync = new();
    private readonly Dictionary<string, ControlSession> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _sessionByConnection = new();
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

        lock (_sync)
        {
            if (_sessionByConnection.ContainsKey(connectionId))
            {
                return ControlAuthResult.Failed(
                    new ControlCommandError(
                        "conflict",
                        "alreadyAuthenticated",
                        "Control connection is already authenticated.",
                        Retryable: false
                    )
                );
            }

            var sessionId = Guid.NewGuid().ToString("N");
            _sessions[sessionId] = new ControlSession(
                sessionId,
                connectionId,
                DateTimeOffset.UtcNow
            );
            _sessionByConnection[connectionId] = sessionId;
            return ControlAuthResult.Succeeded(sessionId);
        }
    }

    public ControlLeaseResult AcquireLease(Guid connectionId, string sessionId, string clientName)
    {
        lock (_sync)
        {
            if (!SessionBelongsToConnectionLocked(connectionId, sessionId, out var session))
            {
                return ControlLeaseResult.Failed(
                    new ControlCommandError(
                        "auth_failed",
                        "unknownSession",
                        "Control session is not authenticated for this connection.",
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
                session.ConnectionId,
                clientName,
                DateTimeOffset.UtcNow
            );
            return ControlLeaseResult.Succeeded(leaseId);
        }
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

            return AcquireLease(session.ConnectionId, sessionId, clientName);
        }
    }

    public void OnDisconnected(Guid connectionId)
    {
        lock (_sync)
        {
            if (!_sessionByConnection.Remove(connectionId, out var sessionId))
                return;

            _sessions.Remove(sessionId);

            if (_activeLease != null && _activeLease.ConnectionId == connectionId)
                _activeLease = null;
        }
    }

    public bool IsLeaseValidForConnection(Guid connectionId, string? leaseId)
    {
        if (!_config.RequireControlLease)
            return true;
        if (string.IsNullOrEmpty(leaseId))
            return false;

        lock (_sync)
        {
            return _activeLease != null
                && _activeLease.ConnectionId == connectionId
                && string.Equals(_activeLease.LeaseId, leaseId, StringComparison.Ordinal);
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

    private bool SessionBelongsToConnectionLocked(
        Guid connectionId,
        string sessionId,
        out ControlSession session
    )
    {
        if (
            _sessionByConnection.TryGetValue(connectionId, out var connectionSessionId)
            && string.Equals(connectionSessionId, sessionId, StringComparison.Ordinal)
            && _sessions.TryGetValue(sessionId, out session)
        )
        {
            return true;
        }

        session = null!;
        return false;
    }

    private sealed record ControlSession(
        string SessionId,
        Guid ConnectionId,
        DateTimeOffset CreatedAt
    );

    private sealed record ControlLease(
        string LeaseId,
        string SessionId,
        Guid ConnectionId,
        string ClientName,
        DateTimeOffset CreatedAt
    );
}
