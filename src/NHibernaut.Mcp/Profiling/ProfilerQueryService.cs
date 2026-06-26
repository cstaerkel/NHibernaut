using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Server;

namespace NHibernaut.Mcp.Profiling;

public sealed class ProfilerQueryService : IProfilerQueryService
{
    private const int MaxSessionResults = 100;
    private const int MaxStatementResults = 100;
    private const int MaxAlerts = 200;
    private const int MaxQueryShapes = 100;
    private const int GlobalAlertFetchCap = 1000;

    private readonly IDashboardClient _client;

    public ProfilerQueryService(IDashboardClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<SessionListResult> ListSessionsAsync(
        int take = 20,
        DateTimeOffset? since = null,
        AlertSeverity? minSeverity = null,
        CancellationToken ct = default)
    {
        var limit = Clamp(take, 1, MaxSessionResults);
        var sessions = await _client.GetSessionsAsync(limit + 1, since, minSeverity, ct).ConfigureAwait(false);
        var truncated = sessions.Count > limit;
        return new SessionListResult(
            sessions.Take(limit).Select(ToSessionSummary).ToList(),
            new ToolPage(limit, truncated, truncated ? "Call nhibernaut_list_sessions with since or a smaller take." : null));
    }

    public async Task<SessionDetailResult?> GetSessionAsync(
        Guid sessionId,
        int maxStatements = 50,
        CancellationToken ct = default)
    {
        var detail = await _client.GetSessionDetailAsync(sessionId, ct).ConfigureAwait(false);
        if (detail is null) return null;

        var limit = Clamp(maxStatements, 1, MaxStatementResults);
        var statements = detail.Statements.Take(limit).Select(ToStatement).ToList();
        return new SessionDetailResult(
            ToSessionSummary(detail.Summary),
            detail.Alerts.Select(a => ToAlertFeedItem(detail.Summary, a).Alert).ToList(),
            statements,
            SummarizeEntityLoads(detail.EntityLoads),
            SummarizeWrites(detail.Writes),
            SummarizeCollectionInits(detail.CollectionInits),
            new ToolPage(limit, detail.Statements.Count > limit, detail.Statements.Count > limit ? "Call nhibernaut_get_statement for a specific statement id or lower max_statements." : null));
    }

    public async Task<AlertListResult> ListAlertsAsync(
        int take = 50,
        AlertSeverity? minSeverity = null,
        string? alertType = null,
        Guid? sessionId = null,
        CancellationToken ct = default)
    {
        var limit = Clamp(take, 1, MaxAlerts);
        IReadOnlyList<McpAlertFeedItem> alerts;

        if (sessionId is { } id)
        {
            var detail = await _client.GetSessionDetailAsync(id, ct).ConfigureAwait(false);
            alerts = detail is null
                ? []
                : detail.Alerts.Select(a => ToAlertFeedItem(detail.Summary, a)).ToList();
        }
        else
        {
            var fetch = Math.Min(GlobalAlertFetchCap, Math.Max(limit * 20, MaxAlerts));
            var feed = await _client.GetAlertsAsync(fetch, ct).ConfigureAwait(false);
            alerts = feed.Select(ToAlertFeedItem).ToList();
        }

        var filtered = alerts
            .Where(a => minSeverity is null || ParseSeverity(a.Alert.Severity) >= minSeverity)
            .Where(a => string.IsNullOrWhiteSpace(alertType) || string.Equals(a.Alert.Type, alertType, StringComparison.OrdinalIgnoreCase))
            .Take(limit)
            .ToList();

        return new AlertListResult(
            filtered,
            new ToolPage(limit, alerts.Count > filtered.Count && filtered.Count == limit, filtered.Count == limit ? "Use alert_type, min_severity, session_id, or a smaller take to narrow the alert feed." : null));
    }

    public async Task<QueryShapeRankResult> RankQueryShapesAsync(
        int limit = 20,
        QueryShapeSortBy sortBy = QueryShapeSortBy.TotalDurationMs,
        int minExecutionCount = 1,
        CancellationToken ct = default)
    {
        var take = Clamp(limit, 1, MaxQueryShapes);
        var aggregate = await _client.GetAggregateAsync(ct).ConfigureAwait(false);
        var sorted = SortQueryShapes(
                aggregate
                    .Where(r => r.ExecutionCount >= Math.Max(minExecutionCount, 1))
                    .Select(ToQueryShape),
                sortBy)
            .Take(take)
            .ToList();

        return new QueryShapeRankResult(sorted, sortBy, new ToolPage(take, aggregate.Count > take, aggregate.Count > take ? "Use a smaller limit or min_execution_count to narrow query shapes." : null));
    }

    public async Task<CompareSessionsResult?> CompareSessionsAsync(
        Guid sessionAId,
        Guid sessionBId,
        bool includeQueryShapeDiffs = true,
        CancellationToken ct = default)
    {
        var sessionA = await _client.GetSessionDetailAsync(sessionAId, ct).ConfigureAwait(false);
        var sessionB = await _client.GetSessionDetailAsync(sessionBId, ct).ConfigureAwait(false);
        if (sessionA is null || sessionB is null) return null;

        var deltas = new List<McpScalarDelta>
        {
            Delta("statement_count", sessionA.Summary.StatementCount, sessionB.Summary.StatementCount),
            Delta("db_time_ms", sessionA.Summary.TotalDurationMs, sessionB.Summary.TotalDurationMs),
            Delta("rows_read", sessionA.Summary.TotalRowsRead, sessionB.Summary.TotalRowsRead),
            Delta("writes", sessionA.Summary.WriteCount, sessionB.Summary.WriteCount),
            Delta("alerts", sessionA.Summary.AlertCount, sessionB.Summary.AlertCount),
        };

        var shapeDiffs = includeQueryShapeDiffs ? CompareQueryShapes(sessionA, sessionB) : [];
        return new CompareSessionsResult(
            sessionAId.ToString(),
            sessionBId.ToString(),
            deltas,
            shapeDiffs,
            new ToolPage(shapeDiffs.Count, false, null));
    }

    public async Task<StatementResult?> GetStatementAsync(
        Guid sessionId,
        Guid statementId,
        CancellationToken ct = default)
    {
        var detail = await _client.GetSessionDetailAsync(sessionId, ct).ConfigureAwait(false);
        var statementIdText = statementId.ToString();
        var statement = detail?.Statements.FirstOrDefault(s => string.Equals(s.Id, statementIdText, StringComparison.OrdinalIgnoreCase));
        if (detail is null || statement is null) return null;

        return new StatementResult(
            ToStatement(statement),
            SummarizeEntityLoads(detail.EntityLoads.Where(e => SameId(e.StatementId, statementIdText))),
            SummarizeWrites(detail.Writes.Where(w => SameId(w.StatementId, statementIdText))),
            SummarizeCollectionInits(detail.CollectionInits.Where(c => SameId(c.StatementId, statementIdText))));
    }

    public async Task<DiagnosticSummaryResult?> SummarizeSessionAsync(
        Guid sessionId,
        DiagnosticFocus focus = DiagnosticFocus.All,
        CancellationToken ct = default)
    {
        var detail = await _client.GetSessionDetailAsync(sessionId, ct).ConfigureAwait(false);
        if (detail is null) return null;

        if (detail.Statements.Count == 0 && detail.Alerts.Count == 0)
        {
            return new DiagnosticSummaryResult(
                sessionId.ToString(),
                focus,
                "No statements or alerts were captured for this session. Generate profiler data with a profiled app or the console sample, then call nhibernaut_list_sessions again.",
                [],
                ["nhibernaut_list_sessions"]);
        }

        var alerts = FilterAlertsByFocus(detail.Alerts, focus).ToList();
        var findings = alerts.Select(ToFinding).ToList();
        var top = findings.FirstOrDefault();
        var summary = top is null
            ? $"Session {sessionId} has {detail.Summary.StatementCount} statements and {detail.Summary.AlertCount} alerts. No focus-specific alerts matched {focus}."
            : $"Session {sessionId} top {focus} finding: {top.Evidence}";
        var nextCalls = findings
            .SelectMany(f => f.StatementIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(id => $"nhibernaut_get_statement session_id={sessionId} statement_id={id}")
            .DefaultIfEmpty($"nhibernaut_get_session session_id={sessionId}")
            .ToList();

        return new DiagnosticSummaryResult(sessionId.ToString(), focus, summary, findings, nextCalls);
    }

    private static McpSessionSummary ToSessionSummary(SessionSummaryDto dto)
        => new(dto.Id, dto.StartedAt, dto.EndedAt, dto.StatementCount, dto.TotalDurationMs, dto.TotalRowsRead, dto.WriteCount, dto.AlertCount, dto.MaxSeverity);

    private static McpAlertFeedItem ToAlertFeedItem(AlertFeedItemDto dto)
        => new(dto.SessionId, dto.SessionStartedAt, ToAlert(dto.Alert));

    private static McpAlertFeedItem ToAlertFeedItem(SessionSummaryDto summary, AlertDto dto)
        => new(summary.Id, summary.StartedAt, ToAlert(dto));

    private static McpAlertItem ToAlert(AlertDto dto)
        => new(dto.Id, dto.Type, dto.Severity, dto.Title, dto.Description, dto.Suggestion, dto.RelatedStatementIds);

    private static McpStatement ToStatement(StatementDto dto)
        => new(
            dto.Id,
            dto.SessionId,
            dto.Sql,
            dto.NormalizedSql,
            dto.Kind,
            dto.StartedAt,
            dto.DurationMs,
            dto.RowsAffected,
            dto.RowsRead,
            dto.Exception,
            dto.StackTrace,
            dto.EntityLoadCount,
            dto.CollectionInitCount,
            dto.Parameters.Select(ToParameter).ToList());

    private static McpParameter ToParameter(ParamDto dto)
        => new(dto.Name, dto.DbType, dto.Value, dto.Size, dto.Direction);

    private static McpQueryShape ToQueryShape(AggregateRowDto dto)
        => new(dto.NormalizedSql, dto.ExecutionCount, dto.TotalDurationMs, dto.AvgDurationMs, dto.MaxRowsRead, dto.SessionCount, dto.NPlusOneIncidence);

    private static IReadOnlyList<McpEntityLoadSummary> SummarizeEntityLoads(IEnumerable<EntityLoadDto> loads)
        => loads
            .GroupBy(e => new { e.EntityType, e.Id, e.StatementId })
            .Select(g => new McpEntityLoadSummary(g.Key.EntityType, g.Key.Id, g.Key.StatementId, g.Count()))
            .ToList();

    private static IReadOnlyList<McpEntityWriteSummary> SummarizeWrites(IEnumerable<EntityWriteDto> writes)
        => writes
            .GroupBy(w => new { w.Kind, w.EntityType, w.Id, w.StatementId, w.NoActualChange })
            .Select(g => new McpEntityWriteSummary(g.Key.Kind, g.Key.EntityType, g.Key.Id, g.Key.StatementId, g.Key.NoActualChange, g.Count()))
            .ToList();

    private static IReadOnlyList<McpCollectionInitSummary> SummarizeCollectionInits(IEnumerable<CollectionInitDto> collectionInits)
        => collectionInits
            .GroupBy(c => new { c.Role, c.StatementId })
            .Select(g => new McpCollectionInitSummary(g.Key.Role, g.Key.StatementId, g.Count()))
            .ToList();

    private static IReadOnlyList<McpQueryShapeDiff> CompareQueryShapes(SessionDetailDto sessionA, SessionDetailDto sessionB)
    {
        var a = GroupShapes(sessionA);
        var b = GroupShapes(sessionB);
        return a.Keys
            .Union(b.Keys, StringComparer.Ordinal)
            .Select(shape =>
            {
                a.TryGetValue(shape, out var aCount);
                b.TryGetValue(shape, out var bCount);
                return new McpQueryShapeDiff(shape, aCount, bCount, bCount - aCount);
            })
            .Where(d => d.Delta != 0)
            .OrderByDescending(d => Math.Abs(d.Delta))
            .ThenBy(d => d.NormalizedSql, StringComparer.Ordinal)
            .ToList();
    }

    private static Dictionary<string, int> GroupShapes(SessionDetailDto detail)
        => detail.Statements
            .Where(s => !string.IsNullOrWhiteSpace(s.NormalizedSql))
            .GroupBy(s => s.NormalizedSql!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static IEnumerable<AlertDto> FilterAlertsByFocus(IEnumerable<AlertDto> alerts, DiagnosticFocus focus)
        => focus switch
        {
            DiagnosticFocus.NPlusOne => alerts.Where(a => Contains(a.Type, "NPlusOne") || Contains(a.Title, "N+1") || Contains(a.Description, "N+1")),
            DiagnosticFocus.Writes => alerts.Where(a => Contains(a.Type, "Write") || Contains(a.Title, "write") || Contains(a.Description, "write")),
            DiagnosticFocus.SlowQueries => alerts.Where(a => Contains(a.Type, "Slow") || Contains(a.Title, "slow") || Contains(a.Description, "slow")),
            DiagnosticFocus.Alerts or DiagnosticFocus.All => alerts,
            _ => alerts,
        };

    private static McpDiagnosticFinding ToFinding(AlertDto alert)
        => new(
            alert.Type,
            alert.Severity,
            $"{alert.Title}: {alert.Description}",
            alert.RelatedStatementIds,
            alert.Suggestion);

    private static IOrderedEnumerable<McpQueryShape> SortQueryShapes(IEnumerable<McpQueryShape> shapes, QueryShapeSortBy sortBy)
        => sortBy switch
        {
            QueryShapeSortBy.AvgDurationMs => shapes.OrderByDescending(s => s.AvgDurationMs),
            QueryShapeSortBy.ExecutionCount => shapes.OrderByDescending(s => s.ExecutionCount),
            QueryShapeSortBy.NPlusOneIncidence => shapes.OrderByDescending(s => s.NPlusOneIncidence),
            _ => shapes.OrderByDescending(s => s.TotalDurationMs),
        };

    private static McpScalarDelta Delta(string name, double a, double b) => new(name, a, b, b - a);

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static AlertSeverity ParseSeverity(string? value)
        => Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var severity) ? severity : AlertSeverity.Info;

    private static bool SameId(string? value, string expected)
        => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string? value, string fragment)
        => value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
}
