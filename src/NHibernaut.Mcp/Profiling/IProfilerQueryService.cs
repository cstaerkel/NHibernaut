using System;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core.Model;
using NHibernaut.Mcp.Contracts;

namespace NHibernaut.Mcp.Profiling;

public interface IProfilerQueryService
{
    Task<SessionListResult> ListSessionsAsync(
        int take = 20,
        DateTimeOffset? since = null,
        AlertSeverity? minSeverity = null,
        CancellationToken ct = default);

    Task<SessionDetailResult?> GetSessionAsync(
        Guid sessionId,
        int maxStatements = 50,
        CancellationToken ct = default);

    Task<AlertListResult> ListAlertsAsync(
        int take = 50,
        AlertSeverity? minSeverity = null,
        string? alertType = null,
        Guid? sessionId = null,
        CancellationToken ct = default);

    Task<QueryShapeRankResult> RankQueryShapesAsync(
        int limit = 20,
        QueryShapeSortBy sortBy = QueryShapeSortBy.TotalDurationMs,
        int minExecutionCount = 1,
        CancellationToken ct = default);

    Task<CompareSessionsResult?> CompareSessionsAsync(
        Guid sessionAId,
        Guid sessionBId,
        bool includeQueryShapeDiffs = true,
        CancellationToken ct = default);

    Task<StatementResult?> GetStatementAsync(
        Guid sessionId,
        Guid statementId,
        CancellationToken ct = default);

    Task<DiagnosticSummaryResult?> SummarizeSessionAsync(
        Guid sessionId,
        DiagnosticFocus focus = DiagnosticFocus.All,
        CancellationToken ct = default);
}
