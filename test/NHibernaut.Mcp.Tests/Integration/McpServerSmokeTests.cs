using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NHibernaut.Mcp.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Mcp.Tests.Integration;

[Collection(McpServerCollection.Name)]
public sealed class McpServerSmokeTests
{
    [Fact]
    public async Task Mcp_server_lists_expected_tools_over_stdio()
    {
        using var dashboard = TestDashboardServer.StartSeeded();
        await using var harness = await McpProcessHarness.StartAsync(dashboard.Url);

        await harness.InitializeAsync();
        var tools = await harness.ListToolsAsync();
        var names = tools.EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("nhibernaut_list_sessions", names);
        Assert.Contains("nhibernaut_get_session", names);
        Assert.Contains("nhibernaut_list_alerts", names);
        Assert.Contains("nhibernaut_rank_query_shapes", names);
        Assert.Contains("nhibernaut_compare_sessions", names);
        Assert.Contains("nhibernaut_get_statement", names);
        Assert.Contains("nhibernaut_summarize_session", names);
    }

    [Fact]
    public async Task Mcp_server_calls_list_sessions_against_seeded_dashboard()
    {
        using var dashboard = TestDashboardServer.StartSeeded();
        await using var harness = await McpProcessHarness.StartAsync(dashboard.Url);

        await harness.InitializeAsync();
        var result = await harness.CallToolAsync("nhibernaut_list_sessions", new { take = 5 });
        var text = McpProcessHarness.TextContent(result);

        Assert.Contains("# NHibernaut Sessions", text, StringComparison.Ordinal);
        Assert.Contains(SampleProfilerData.SessionAId.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_server_rejects_unauthorized_dashboard_without_echoing_token()
    {
        const string dashboardToken = "opaque-dashboard-token";
        using var dashboard = TestDashboardServer.StartSeeded(dashboardToken);
        await using var harness = await McpProcessHarness.StartAsync(dashboard.Url);

        await harness.InitializeAsync();
        var result = await harness.CallToolAsync("nhibernaut_list_sessions", new { take = 5 });
        var text = McpProcessHarness.TextContent(result);

        Assert.Contains("requires a token", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NHIBERNAUT_DASHBOARD_TOKEN", text, StringComparison.Ordinal);
        Assert.DoesNotContain(dashboardToken, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mcp_server_stdout_contains_only_json_rpc_frames()
    {
        using var dashboard = TestDashboardServer.StartSeeded();
        await using var harness = await McpProcessHarness.StartAsync(dashboard.Url);

        await harness.InitializeAsync();
        await harness.ListToolsAsync();
        await harness.CallToolAsync("nhibernaut_list_sessions", new { take = 1 });

        Assert.NotEmpty(harness.StdoutLines);
        foreach (var line in harness.StdoutLines)
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        }
    }

    [Fact]
    public async Task Mcp_server_logs_diagnostics_to_stderr()
    {
        using var dashboard = TestDashboardServer.StartSeeded();
        await using var harness = await McpProcessHarness.StartAsync(dashboard.Url);

        await harness.InitializeAsync();

        Assert.Contains(harness.StderrLines, line => line.Contains("NHibernaut MCP server", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.StdoutLines, line => line.Contains("NHibernaut MCP server", StringComparison.Ordinal));
    }
}
