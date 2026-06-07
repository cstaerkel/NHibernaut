using System;
using System.Collections.Generic;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Storage;

/// <summary>
/// Pluggable store of recent profiled sessions. The default is <see cref="InMemoryProfilerStore"/>;
/// consumers can supply custom sinks (file, OTLP, etc.).
/// </summary>
public interface IProfilerStore
{
    /// <summary>Returns the (mutable) session for the id, creating and tracking it if new.</summary>
    ProfiledSession GetOrCreateSession(Guid sessionId);

    /// <summary>Returns the session for the id, or null if unknown/evicted.</summary>
    ProfiledSession? GetSession(Guid sessionId);

    /// <summary>Most-recently-started sessions first, up to <paramref name="take"/>.</summary>
    IReadOnlyList<ProfiledSession> GetRecentSessions(int take);

    /// <summary>Marks a session ended and sealed; raises <see cref="SessionSealed"/>.</summary>
    void SealSession(Guid sessionId);

    /// <summary>Removes all sessions.</summary>
    void Clear();

    /// <summary>Number of sessions currently retained.</summary>
    int Count { get; }

    /// <summary>Raised when a session is sealed (drives analysis finalization and the live feed).</summary>
    event Action<ProfiledSession>? SessionSealed;
}
