using System;
using System.Collections.Generic;
using System.Linq;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Storage;

/// <summary>
/// Thread-safe, bounded in-memory store keyed by session id. Retention is by count and by age;
/// the oldest sessions are pruned first. Keeping the last N sessions is what enables the compare
/// feature in the dashboard.
/// </summary>
public sealed class InMemoryProfilerStore : IProfilerStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ProfiledSession> _byId = new();
    private readonly LinkedList<Guid> _order = new(); // front = oldest, back = newest
    private readonly NHibernautOptions _options;
    private readonly Func<DateTimeOffset> _clock;

    public InMemoryProfilerStore()
        : this(new NHibernautOptions())
    {
    }

    public InMemoryProfilerStore(NHibernautOptions options, Func<DateTimeOffset>? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public event Action<ProfiledSession>? SessionSealed;

    public ProfiledSession GetOrCreateSession(Guid sessionId)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(sessionId, out var existing))
                return existing;

            var session = new ProfiledSession
            {
                Id = sessionId,
                StartedAt = _clock(),
                RequestId = NHibernautRequestContext.CurrentRequestId // Tier C correlation (null otherwise)
            };
            _byId.Add(sessionId, session);
            _order.AddLast(sessionId);
            Prune();
            return session;
        }
    }

    public ProfiledSession? GetSession(Guid sessionId)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    public IReadOnlyList<ProfiledSession> GetRecentSessions(int take)
    {
        if (take <= 0) return Array.Empty<ProfiledSession>();

        lock (_gate)
        {
            var result = new List<ProfiledSession>(Math.Min(take, _order.Count));
            for (var node = _order.Last; node is not null && result.Count < take; node = node.Previous)
            {
                if (_byId.TryGetValue(node.Value, out var session))
                    result.Add(session);
            }
            return result;
        }
    }

    public void SealSession(Guid sessionId)
    {
        ProfiledSession? session;
        lock (_gate)
        {
            if (!_byId.TryGetValue(sessionId, out session))
                return;

            // EndedAt advances to the latest close so multi-transaction sessions (which release the
            // connection per transaction under the default release mode) report their true end time.
            session.EndedAt = _clock();
            session.IsSealed = true;
        }

        // Raise outside the lock so subscribers (analysis, SSE) can't deadlock against capture.
        SessionSealed?.Invoke(session);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _byId.Clear();
            _order.Clear();
        }
    }

    public int Count
    {
        get { lock (_gate) { return _byId.Count; } }
    }

    // Must be called under _gate.
    private void Prune()
    {
        var now = _clock();

        // Age-based: drop stale sessions from the oldest end.
        while (_order.First is { } oldest && _byId.TryGetValue(oldest.Value, out var session))
        {
            var reference = session.EndedAt ?? session.StartedAt;
            if (now - reference <= _options.RetentionMaxAge) break;
            _order.RemoveFirst();
            _byId.Remove(oldest.Value);
        }

        // Count-based: trim oldest until within the cap.
        while (_byId.Count > _options.RetentionSessionCount && _order.First is { } oldest)
        {
            _order.RemoveFirst();
            _byId.Remove(oldest.Value);
        }
    }
}
