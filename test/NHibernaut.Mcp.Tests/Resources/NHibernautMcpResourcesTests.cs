using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Resources;
using NHibernaut.Mcp.Tests.Infrastructure;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.Mcp.Tests.Resources;

public sealed class NHibernautMcpResourcesTests
{
    [Fact]
    public void Config_resource_excludes_token()
    {
        var resources = Resources(
            SampleProfilerData.Dashboard(),
            new NHibernautMcpOptions
            {
                DashboardUrl = "http://127.0.0.1:5005",
                DashboardToken = "opaque-token-value",
            });

        var content = resources.Config();

        Assert.Equal(NHibernautMcpResources.ConfigUri, content.Uri);
        Assert.Contains("http://127.0.0.1:5005", content.Text, StringComparison.Ordinal);
        Assert.Contains("authentication: configured", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("opaque-token-value", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sessions_resource_returns_bounded_recent_sessions()
    {
        var dashboard = SampleProfilerData.Dashboard();
        var resources = Resources(dashboard);

        var content = await resources.SessionsAsync();

        Assert.Equal(21, dashboard.LastSessionsTake);
        Assert.Equal(NHibernautMcpResources.SessionsUri, content.Uri);
        Assert.Contains("# NHibernaut Sessions", content.Text, StringComparison.Ordinal);
        Assert.Contains(SampleProfilerData.SessionAId.ToString(), content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_detail_resource_uses_same_sensitive_defaults_as_get_session()
    {
        var resources = Resources(SampleProfilerData.Dashboard());

        var content = await resources.SessionAsync(SampleProfilerData.SessionAId.ToString());

        Assert.Equal($"nhibernaut://sessions/{SampleProfilerData.SessionAId}", content.Uri);
        Assert.Contains("parameters hidden", content.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("p0 Int32: 42", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("at App.Repository.LoadWidget()", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Aggregate_resource_returns_bounded_query_shapes()
    {
        var dashboard = new FakeDashboardClient();
        for (var i = 0; i < 30; i++)
        {
            dashboard.AggregateRows.Add(new AggregateRowDto($"SELECT * FROM table_{i} WHERE id = ?", i + 1, i + 1, i + 1, i, 1, i % 2));
        }
        var resources = Resources(dashboard);

        var content = await resources.AggregateAsync();

        Assert.Equal(NHibernautMcpResources.AggregateUri, content.Uri);
        Assert.Contains("# Query Shapes", content.Text, StringComparison.Ordinal);
        Assert.Contains("table_29", content.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("table_9", content.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Alerts_resource_returns_bounded_alert_feed()
    {
        var dashboard = SampleProfilerData.Dashboard();
        var resources = Resources(dashboard);

        var content = await resources.AlertsAsync();

        Assert.Equal(NHibernautMcpResources.AlertsUri, content.Uri);
        Assert.Contains("# NHibernaut Alerts", content.Text, StringComparison.Ordinal);
        Assert.Contains("SelectNPlusOne", content.Text, StringComparison.Ordinal);
        Assert.True(dashboard.LastAlertsTake <= 1000);
    }

    [Fact]
    public async Task Resource_output_is_truncated_with_follow_up_hint()
    {
        var resources = Resources(
            SampleProfilerData.Dashboard(),
            new NHibernautMcpOptions { MaxOutputChars = 80 });

        var content = await resources.SessionAsync(SampleProfilerData.SessionAId.ToString());

        Assert.Contains("truncated: true", content.Text, StringComparison.Ordinal);
        Assert.Contains("Call the same tool", content.Text, StringComparison.Ordinal);
    }

    private static NHibernautMcpResources Resources(
        FakeDashboardClient dashboard,
        NHibernautMcpOptions? options = null)
    {
        var actualOptions = options ?? new NHibernautMcpOptions();
        return new NHibernautMcpResources(
            new ProfilerQueryService(dashboard),
            new McpResponseFormatter(actualOptions),
            actualOptions);
    }
}
