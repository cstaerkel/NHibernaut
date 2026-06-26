using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core.Model;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Tools;
using Xunit;

namespace NHibernaut.Mcp.Tests.Tools;

public sealed class NHibernautMcpToolsTests
{
    [Fact]
    public async Task ListSessions_calls_query_service_with_clamped_take()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        await tools.ListSessionsAsync(take: 500);

        Assert.Equal(100, service.ListSessionsTake);
    }

    [Fact]
    public async Task GetSession_rejects_invalid_guid()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        var text = await tools.GetSessionAsync("not-a-guid");

        Assert.Contains("Invalid session_id", text, StringComparison.Ordinal);
        Assert.Equal(0, service.GetSessionCallCount);
    }

    [Fact]
    public async Task ListAlerts_passes_filters_to_query_service()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        await tools.ListAlertsAsync(25, "warning", "SelectNPlusOne", SessionId.ToString());

        Assert.Equal(AlertSeverity.Warning, service.ListAlertsMinSeverity);
        Assert.Equal("SelectNPlusOne", service.ListAlertsType);
        Assert.Equal(SessionId, service.ListAlertsSessionId);
    }

    [Fact]
    public async Task RankQueryShapes_uses_requested_sort()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        await tools.RankQueryShapesAsync(sort_by: "avg_duration_ms");

        Assert.Equal(QueryShapeSortBy.AvgDurationMs, service.RankSortBy);
    }

    [Fact]
    public async Task CompareSessions_rejects_invalid_a_or_b_id()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        var text = await tools.CompareSessionsAsync("not-a-guid", SessionId.ToString());

        Assert.Contains("Invalid session_a_id", text, StringComparison.Ordinal);
        Assert.Equal(0, service.CompareCallCount);
    }

    [Fact]
    public async Task GetStatement_requires_valid_session_and_statement_ids()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        var text = await tools.GetStatementAsync(SessionId.ToString(), "not-a-guid");

        Assert.Contains("Invalid statement_id", text, StringComparison.Ordinal);
        Assert.Equal(0, service.GetStatementCallCount);
    }

    [Fact]
    public async Task SummarizeSession_passes_focus_to_query_service()
    {
        var service = new FakeProfilerQueryService();
        var tools = Tools(service);

        await tools.SummarizeSessionAsync(SessionId.ToString(), "n_plus_one");

        Assert.Equal(DiagnosticFocus.NPlusOne, service.SummaryFocus);
    }

    [Fact]
    public async Task Every_tool_uses_formatter_for_markdown_and_json()
    {
        var tools = Tools(new FakeProfilerQueryService());

        var markdown = await tools.ListSessionsAsync(response_format: "markdown");
        var json = await tools.ListSessionsAsync(response_format: "json");

        Assert.Contains("# NHibernaut Sessions", markdown, StringComparison.Ordinal);
        Assert.Contains("\"sessions\"", json, StringComparison.Ordinal);
    }

    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StatementId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");

    private static NHibernautMcpTools Tools(FakeProfilerQueryService service)
        => new(service, new McpResponseFormatter(new NHibernautMcpOptions()), new NHibernautMcpOptions());

    private sealed class FakeProfilerQueryService : IProfilerQueryService
    {
        public int? ListSessionsTake { get; private set; }
        public AlertSeverity? ListAlertsMinSeverity { get; private set; }
        public string? ListAlertsType { get; private set; }
        public Guid? ListAlertsSessionId { get; private set; }
        public QueryShapeSortBy RankSortBy { get; private set; }
        public DiagnosticFocus SummaryFocus { get; private set; }
        public int GetSessionCallCount { get; private set; }
        public int CompareCallCount { get; private set; }
        public int GetStatementCallCount { get; private set; }

        public Task<SessionListResult> ListSessionsAsync(int take = 20, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default)
        {
            ListSessionsTake = take;
            return Task.FromResult(new SessionListResult(
                [new McpSessionSummary(SessionId.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 2, 3, 0, 0, null)],
                new ToolPage(take, false, null)));
        }

        public Task<SessionDetailResult?> GetSessionAsync(Guid sessionId, int maxStatements = 50, CancellationToken ct = default)
        {
            GetSessionCallCount++;
            return Task.FromResult<SessionDetailResult?>(new SessionDetailResult(
                new McpSessionSummary(sessionId.ToString(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 2, 3, 0, 0, null),
                [],
                [Statement(sessionId)],
                [],
                [],
                [],
                new ToolPage(maxStatements, false, null)));
        }

        public Task<AlertListResult> ListAlertsAsync(int take = 50, AlertSeverity? minSeverity = null, string? alertType = null, Guid? sessionId = null, CancellationToken ct = default)
        {
            ListAlertsMinSeverity = minSeverity;
            ListAlertsType = alertType;
            ListAlertsSessionId = sessionId;
            return Task.FromResult(new AlertListResult([], new ToolPage(take, false, null)));
        }

        public Task<QueryShapeRankResult> RankQueryShapesAsync(int limit = 20, QueryShapeSortBy sortBy = QueryShapeSortBy.TotalDurationMs, int minExecutionCount = 1, CancellationToken ct = default)
        {
            RankSortBy = sortBy;
            return Task.FromResult(new QueryShapeRankResult([], sortBy, new ToolPage(limit, false, null)));
        }

        public Task<CompareSessionsResult?> CompareSessionsAsync(Guid sessionAId, Guid sessionBId, bool includeQueryShapeDiffs = true, CancellationToken ct = default)
        {
            CompareCallCount++;
            return Task.FromResult<CompareSessionsResult?>(new CompareSessionsResult(sessionAId.ToString(), sessionBId.ToString(), [], [], new ToolPage(0, false, null)));
        }

        public Task<StatementResult?> GetStatementAsync(Guid sessionId, Guid statementId, CancellationToken ct = default)
        {
            GetStatementCallCount++;
            return Task.FromResult<StatementResult?>(new StatementResult(Statement(sessionId), [], [], []));
        }

        public Task<DiagnosticSummaryResult?> SummarizeSessionAsync(Guid sessionId, DiagnosticFocus focus = DiagnosticFocus.All, CancellationToken ct = default)
        {
            SummaryFocus = focus;
            return Task.FromResult<DiagnosticSummaryResult?>(new DiagnosticSummaryResult(sessionId.ToString(), focus, "summary", [], ["nhibernaut_get_session"]));
        }

        private static McpStatement Statement(Guid sessionId)
            => new(
                StatementId.ToString(),
                sessionId.ToString(),
                "SELECT 1",
                "SELECT ?",
                "Select",
                DateTimeOffset.UtcNow,
                1,
                null,
                1,
                null,
                null,
                0,
                0,
                []);
    }
}
