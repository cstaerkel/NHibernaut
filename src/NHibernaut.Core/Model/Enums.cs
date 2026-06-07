namespace NHibernaut.Core.Model;

/// <summary>The kind of SQL statement, derived from its leading keyword.</summary>
public enum StatementKind
{
    Select,
    Insert,
    Update,
    Delete,
    Other
}

/// <summary>The kind of entity write captured from a post-commit/flush event.</summary>
public enum WriteKind
{
    Insert,
    Update,
    Delete
}

/// <summary>The completion outcome of a database transaction.</summary>
public enum TransactionOutcome
{
    Unknown,
    Commit,
    Rollback
}

/// <summary>Alert severity, ordered so the maximum is meaningful (Info &lt; Warning &lt; Error).</summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
