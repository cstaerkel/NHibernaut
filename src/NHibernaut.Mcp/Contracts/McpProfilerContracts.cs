using System;
using System.Collections.Generic;

namespace NHibernaut.Mcp.Contracts;

public enum McpResponseFormat { Markdown, Json }

public enum QueryShapeSortBy
{
    TotalDurationMs,
    AvgDurationMs,
    ExecutionCount,
    NPlusOneIncidence,
}

public enum DiagnosticFocus
{
    Alerts,
    NPlusOne,
    Writes,
    SlowQueries,
    All,
}

public sealed record ToolPage(int Limit, bool Truncated, string? ContinuationHint);

public sealed record SessionListResult(
    IReadOnlyList<McpSessionSummary> Sessions,
    ToolPage Page);

public sealed record McpSessionSummary(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int StatementCount,
    double TotalDurationMs,
    int TotalRowsRead,
    int WriteCount,
    int AlertCount,
    string? MaxSeverity);

public sealed record SessionDetailResult(
    McpSessionSummary Summary,
    IReadOnlyList<McpAlertItem> Alerts,
    IReadOnlyList<McpStatement> Statements,
    IReadOnlyList<McpEntityLoadSummary> EntityLoads,
    IReadOnlyList<McpEntityWriteSummary> Writes,
    IReadOnlyList<McpCollectionInitSummary> CollectionInits,
    ToolPage Page);

public sealed record AlertListResult(
    IReadOnlyList<McpAlertFeedItem> Alerts,
    ToolPage Page);

public sealed record McpAlertFeedItem(
    string SessionId,
    DateTimeOffset SessionStartedAt,
    McpAlertItem Alert);

public sealed record McpAlertItem(
    string Id,
    string Type,
    string Severity,
    string Title,
    string Description,
    string? Suggestion,
    IReadOnlyList<string> RelatedStatementIds);

public sealed record QueryShapeRankResult(
    IReadOnlyList<McpQueryShape> Shapes,
    QueryShapeSortBy SortBy,
    ToolPage Page);

public sealed record McpQueryShape(
    string NormalizedSql,
    int ExecutionCount,
    double TotalDurationMs,
    double AvgDurationMs,
    int MaxRowsRead,
    int SessionCount,
    int NPlusOneIncidence);

public sealed record StatementResult(
    McpStatement Statement,
    IReadOnlyList<McpEntityLoadSummary> EntityLoads,
    IReadOnlyList<McpEntityWriteSummary> Writes,
    IReadOnlyList<McpCollectionInitSummary> CollectionInits);

public sealed record McpStatement(
    string Id,
    string SessionId,
    string? Sql,
    string? NormalizedSql,
    string Kind,
    DateTimeOffset StartedAt,
    double DurationMs,
    int? RowsAffected,
    int? RowsRead,
    string? Exception,
    string? StackTrace,
    int EntityLoadCount,
    int CollectionInitCount,
    IReadOnlyList<McpParameter> Parameters);

public sealed record McpParameter(
    string? Name,
    string? DbType,
    string? Value,
    int Size,
    string Direction);

public sealed record McpEntityLoadSummary(
    string EntityType,
    string? Id,
    string? StatementId,
    int Count);

public sealed record McpEntityWriteSummary(
    string Kind,
    string EntityType,
    string? Id,
    string? StatementId,
    bool NoActualChange,
    int Count);

public sealed record McpCollectionInitSummary(
    string Role,
    string? StatementId,
    int Count);

public sealed record CompareSessionsResult(
    string SessionAId,
    string SessionBId,
    IReadOnlyList<McpScalarDelta> Deltas,
    IReadOnlyList<McpQueryShapeDiff> QueryShapeDiffs,
    ToolPage Page);

public sealed record McpScalarDelta(string Name, double SessionAValue, double SessionBValue, double Delta);

public sealed record McpQueryShapeDiff(
    string NormalizedSql,
    int SessionACount,
    int SessionBCount,
    int Delta);

public sealed record DiagnosticSummaryResult(
    string SessionId,
    DiagnosticFocus Focus,
    string Summary,
    IReadOnlyList<McpDiagnosticFinding> Findings,
    IReadOnlyList<string> NextCalls);

public sealed record McpDiagnosticFinding(
    string Category,
    string Severity,
    string Evidence,
    IReadOnlyList<string> StatementIds,
    string? Suggestion);
