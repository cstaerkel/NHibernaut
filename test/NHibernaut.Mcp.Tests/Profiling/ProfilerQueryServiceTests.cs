using System;
using System.Linq;
using System.Threading.Tasks;
using NHibernaut.Core.Model;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Tests.Infrastructure;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.Mcp.Tests.Profiling;

public sealed class ProfilerQueryServiceTests
{
    [Fact]
    public async Task ListSessionsAsync_clamps_take_to_100()
    {
        var client = SampleProfilerData.Dashboard();
        var service = new ProfilerQueryService(client);

        var result = await service.ListSessionsAsync(250);

        Assert.Equal(101, client.LastSessionsTake);
        Assert.Equal(3, result.Sessions.Count);
        Assert.Equal(100, result.Page.Limit);
    }

    [Fact]
    public async Task ListSessionsAsync_fetches_one_extra_session_to_detect_truncation()
    {
        var client = SampleProfilerData.Dashboard();
        client.Sessions.Clear();
        for (var i = 0; i < 101; i++)
        {
            client.Sessions.Add(new SessionSummaryDto(
                Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow.AddSeconds(-i),
                DateTimeOffset.UtcNow.AddSeconds(-i + 1),
                true,
                1,
                1,
                1,
                0,
                0,
                null,
                1));
        }
        var service = new ProfilerQueryService(client);

        var result = await service.ListSessionsAsync(100);

        Assert.Equal(101, client.LastSessionsTake);
        Assert.Equal(100, result.Sessions.Count);
        Assert.True(result.Page.Truncated);
        Assert.Contains("nhibernaut_list_sessions", result.Page.ContinuationHint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSessionAsync_returns_null_for_unknown_session()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.GetSessionAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListAlertsAsync_applies_take_after_filtering()
    {
        var client = SampleProfilerData.Dashboard();
        var service = new ProfilerQueryService(client);

        var result = await service.ListAlertsAsync(2, AlertSeverity.Warning, "SelectNPlusOne");

        Assert.Equal(2, result.Alerts.Count);
        Assert.All(result.Alerts, item => Assert.Equal("SelectNPlusOne", item.Alert.Type));
        Assert.All(result.Alerts, item => Assert.Equal("Warning", item.Alert.Severity));
        Assert.True(client.LastAlertsTake > 2);
    }

    [Fact]
    public async Task ListAlertsAsync_uses_session_detail_when_session_id_is_supplied()
    {
        var client = SampleProfilerData.Dashboard();
        client.AlertFeed.Clear();
        var service = new ProfilerQueryService(client);

        var result = await service.ListAlertsAsync(10, sessionId: SampleProfilerData.SessionAId);

        Assert.Single(result.Alerts);
        Assert.Equal("alert-nplusone", result.Alerts[0].Alert.Id);
        Assert.Equal(1, client.SessionDetailCallCount);
        Assert.Equal(0, client.AlertsCallCount);
    }

    [Fact]
    public async Task RankQueryShapesAsync_sorts_by_requested_metric()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.RankQueryShapesAsync(10, QueryShapeSortBy.AvgDurationMs);

        Assert.Equal("UPDATE widgets SET name = ?", result.Shapes[0].NormalizedSql);
        Assert.Equal(QueryShapeSortBy.AvgDurationMs, result.SortBy);
    }

    [Fact]
    public async Task CompareSessionsAsync_computes_scalar_deltas_from_summaries()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.CompareSessionsAsync(SampleProfilerData.SessionAId, SampleProfilerData.SessionBId);

        Assert.NotNull(result);
        Assert.Contains(result!.Deltas, d => d.Name == "statement_count" && d.SessionAValue == 3 && d.SessionBValue == 2 && d.Delta == -1);
        Assert.Contains(result.Deltas, d => d.Name == "db_time_ms" && d.SessionAValue == 42 && d.SessionBValue == 95 && d.Delta == 53);
    }

    [Fact]
    public async Task CompareSessionsAsync_computes_shape_diffs_from_each_session_detail()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.CompareSessionsAsync(SampleProfilerData.SessionAId, SampleProfilerData.SessionBId);

        Assert.NotNull(result);
        Assert.Contains(result!.QueryShapeDiffs, d => d.NormalizedSql == "SELECT * FROM orders WHERE customer_id = ?" && d.SessionACount == 2 && d.SessionBCount == 0 && d.Delta == -2);
        Assert.Contains(result.QueryShapeDiffs, d => d.NormalizedSql == "DELETE FROM widgets WHERE id = ?" && d.SessionACount == 0 && d.SessionBCount == 1 && d.Delta == 1);
    }

    [Fact]
    public async Task CompareSessionsAsync_does_not_call_global_aggregate()
    {
        var client = SampleProfilerData.Dashboard();
        var service = new ProfilerQueryService(client);

        await service.CompareSessionsAsync(SampleProfilerData.SessionAId, SampleProfilerData.SessionBId);

        Assert.Equal(0, client.AggregateCallCount);
    }

    [Fact]
    public async Task GetStatementAsync_returns_statement_and_related_entities()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.GetStatementAsync(SampleProfilerData.SessionAId, SampleProfilerData.StatementA1Id);

        Assert.NotNull(result);
        Assert.Equal(SampleProfilerData.StatementA1Id.ToString(), result!.Statement.Id);
        Assert.Single(result.EntityLoads);
        Assert.Single(result.Writes);
        Assert.Empty(result.CollectionInits);
    }

    [Fact]
    public async Task GetStatementAsync_returns_null_for_unknown_statement()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.GetStatementAsync(SampleProfilerData.SessionAId, Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task SummarizeSessionAsync_focus_alerts_prioritizes_alert_titles_and_suggestions()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.SummarizeSessionAsync(SampleProfilerData.SessionAId, DiagnosticFocus.Alerts);

        Assert.NotNull(result);
        Assert.Contains("Possible N+1", result!.Summary, StringComparison.Ordinal);
        Assert.Contains(result.Findings, f => f.Suggestion == "Use fetch joins, batch fetching, or projection.");
    }

    [Fact]
    public async Task SummarizeSessionAsync_focus_n_plus_one_lists_related_statement_ids()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.SummarizeSessionAsync(SampleProfilerData.SessionAId, DiagnosticFocus.NPlusOne);

        Assert.NotNull(result);
        Assert.Contains(result!.Findings, f => f.StatementIds.Contains(SampleProfilerData.StatementA2Id.ToString()));
        Assert.Contains("nhibernaut_get_statement", result.NextCalls[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeSessionAsync_empty_session_explains_how_to_generate_data()
    {
        var service = new ProfilerQueryService(SampleProfilerData.Dashboard());

        var result = await service.SummarizeSessionAsync(SampleProfilerData.EmptySessionId, DiagnosticFocus.All);

        Assert.NotNull(result);
        Assert.Contains("No statements or alerts", result!.Summary, StringComparison.Ordinal);
        Assert.Contains("console sample", result.Summary, StringComparison.OrdinalIgnoreCase);
    }
}
