using System;

namespace NHibernaut.Core.Model;

/// <summary>A connection opened during a session, with its lifetime and the statements run on it.</summary>
public sealed class ProfiledConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public System.Collections.Generic.List<Guid> StatementIds { get; } = new();
}

/// <summary>A transaction begun during a session and its eventual outcome.</summary>
public sealed class ProfiledTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset BeganAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public TransactionOutcome Outcome { get; set; } = TransactionOutcome.Unknown;
}

/// <summary>An entity hydrated (loaded) during a session.</summary>
public sealed class EntityLoad
{
    public string EntityType { get; set; } = string.Empty;
    public object? Id { get; set; }

    /// <summary>The statement this load is attributed to, if any.</summary>
    public Guid? StatementId { get; set; }
}

/// <summary>An entity write (insert/update/delete) flushed during a session.</summary>
public sealed class EntityWrite
{
    public WriteKind Kind { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public object? Id { get; set; }

    /// <summary>The statement (INSERT/UPDATE/DELETE) this write is attributed to, if any.</summary>
    public Guid? StatementId { get; set; }

    /// <summary>For updates: true when no tracked property actually changed (best-effort).</summary>
    public bool NoActualChange { get; set; }
}

/// <summary>A lazy collection/proxy that was initialized during a session (primary N+1 signal).</summary>
public sealed class CollectionInit
{
    public string Role { get; set; } = string.Empty;

    /// <summary>The statement this initialization is attributed to, if any.</summary>
    public Guid? StatementId { get; set; }
}
