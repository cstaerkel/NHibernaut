using System;
using System.Collections.Generic;
using System.Linq;

namespace NHibernaut.Core.Model;

/// <summary>
/// The central unit of work: an NHibernate session and everything captured under it. Open sessions
/// are mutated by capture code (under <see cref="SyncRoot"/>); on session end they are sealed and the
/// snapshot is what the dashboard reads.
/// </summary>
public sealed class ProfiledSession
{
    /// <summary>Equal to the NHibernate session id.</summary>
    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>The Tier C request this session's first activity occurred under, if any.</summary>
    public string? RequestId { get; set; }

    /// <summary>Managed thread ids observed executing under this session.</summary>
    public HashSet<int> ThreadIds { get; } = new();

    public List<ProfiledConnection> Connections { get; } = new();
    public List<ProfiledTransaction> Transactions { get; } = new();
    public List<ProfiledStatement> Statements { get; } = new();
    public List<EntityLoad> EntityLoads { get; } = new();
    public List<EntityWrite> Writes { get; } = new();
    public List<CollectionInit> CollectionInits { get; } = new();
    public List<Alert> Alerts { get; } = new();

    /// <summary>True once the session has ended and its alerts have been finalized.</summary>
    public bool IsSealed { get; set; }

    /// <summary>Lock guarding all mutable collections; capture and snapshot operations take it.</summary>
    public object SyncRoot { get; } = new();

    // ---- derived aggregates ----

    public int StatementCount => Statements.Count;

    public double TotalDurationMs => Statements.Sum(s => s.DurationMs);

    public int TotalRowsRead => Statements.Sum(s => s.RowsRead ?? 0);

    public int WriteCount => Writes.Count;

    public AlertSeverity? MaxSeverity => Alerts.Count == 0 ? null : Alerts.Max(a => a.Severity);

    public IDictionary<string, int> EntityCountsByType =>
        EntityLoads.GroupBy(e => e.EntityType).ToDictionary(g => g.Key, g => g.Count());
}
