using System;
using System.Collections.Generic;
using System.Data;

namespace NHibernaut.Core.Model;

/// <summary>A captured copy of a single ADO.NET command parameter.</summary>
public sealed class ParamCapture
{
    public string? Name { get; set; }
    public string? DbType { get; set; }
    public object? Value { get; set; }
    public int Size { get; set; }
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;
}

/// <summary>A single executed SQL statement and everything captured about it.</summary>
public sealed class ProfiledStatement
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The NHibernate session this statement was executed under (Guid.Empty if unknown).</summary>
    public Guid SessionId { get; set; }

    /// <summary>Raw SQL as executed.</summary>
    public string Sql { get; set; } = string.Empty;

    /// <summary>SQL with literals/parameters replaced by a canonical token, for grouping by shape.</summary>
    public string? NormalizedSql { get; set; }

    public List<ParamCapture> Parameters { get; } = new();

    public DateTimeOffset StartedAt { get; set; }

    public double DurationMs { get; set; }

    /// <summary>Rows affected for a non-query (insert/update/delete), if applicable.</summary>
    public int? RowsAffected { get; set; }

    /// <summary>Rows read through the data reader, if applicable.</summary>
    public int? RowsRead { get; set; }

    public StatementKind Kind { get; set; } = StatementKind.Other;

    /// <summary>The exception message if the statement threw (the exception is rethrown unchanged).</summary>
    public string? Exception { get; set; }

    /// <summary>Filtered stack trace of the first app frame(s), if capture is enabled.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Number of entities hydrated while this statement's reader was open.</summary>
    public int EntityLoadCount { get; set; }

    /// <summary>Number of lazy collections initialized while this statement's reader was open.</summary>
    public int CollectionInitCount { get; set; }
}
