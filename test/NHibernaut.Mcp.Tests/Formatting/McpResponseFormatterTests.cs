using System;
using System.Text.Json;
using System.Threading.Tasks;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Mcp.Tests.Formatting;

public sealed class McpResponseFormatterTests
{
    [Fact]
    public async Task Formats_session_list_as_compact_markdown()
    {
        var formatter = Formatter();
        var result = await Service().ListSessionsAsync(20);

        var text = formatter.FormatSessionList(result, McpResponseFormat.Markdown);

        Assert.Contains(SampleProfilerData.SessionAId.ToString(), text, StringComparison.Ordinal);
        Assert.Contains("statements", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nhibernaut_get_session", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formats_session_list_includes_page_continuation_hint()
    {
        var formatter = Formatter();
        var result = await Service().ListSessionsAsync(1);

        var text = formatter.FormatSessionList(result, McpResponseFormat.Markdown);

        Assert.Contains("truncated: true", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_list_sessions", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formats_session_detail_without_parameters_by_default()
    {
        var formatter = Formatter();
        var result = await Service().GetSessionAsync(SampleProfilerData.SessionAId);

        var text = formatter.FormatSessionDetail(result!, McpResponseFormat.Markdown, SensitiveDataRequest.Default);

        Assert.DoesNotContain("p0 Int32: 42", text, StringComparison.Ordinal);
        Assert.Contains("parameters hidden", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Formats_alert_feed_with_related_statement_ids()
    {
        var formatter = Formatter();
        var result = await Service().ListAlertsAsync(10);

        var text = formatter.FormatAlertList(result, McpResponseFormat.Markdown);

        Assert.Contains("SelectNPlusOne", text, StringComparison.Ordinal);
        Assert.Contains(SampleProfilerData.StatementA2Id.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formats_compare_result_with_positive_and_negative_deltas()
    {
        var formatter = Formatter();
        var result = await Service().CompareSessionsAsync(SampleProfilerData.SessionAId, SampleProfilerData.SessionBId);

        var text = formatter.FormatCompare(result!, McpResponseFormat.Markdown);

        Assert.Contains("+53", text, StringComparison.Ordinal);
        Assert.Contains("-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formats_json_with_stable_camel_case_fields()
    {
        var formatter = Formatter();
        var result = await Service().ListSessionsAsync(20);

        var json = formatter.FormatSessionList(result, McpResponseFormat.Json);
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("sessions", out var sessions));
        Assert.True(sessions[0].TryGetProperty("statementCount", out _));
        Assert.False(sessions[0].TryGetProperty("StatementCount", out _));
    }

    [Fact]
    public async Task Formatter_includes_truncation_metadata()
    {
        var formatter = Formatter(maxOutputChars: 80);
        var result = await Service().GetSessionAsync(SampleProfilerData.SessionAId);

        var text = formatter.FormatSessionDetail(result!, McpResponseFormat.Markdown, SensitiveDataRequest.Default);

        Assert.Contains("truncated: true", text, StringComparison.Ordinal);
        Assert.Contains("Call the same tool", text, StringComparison.Ordinal);
    }

    private static ProfilerQueryService Service() => new(SampleProfilerData.Dashboard());

    private static McpResponseFormatter Formatter(int maxOutputChars = 25000)
        => new(new NHibernautMcpOptions { MaxOutputChars = maxOutputChars });
}
