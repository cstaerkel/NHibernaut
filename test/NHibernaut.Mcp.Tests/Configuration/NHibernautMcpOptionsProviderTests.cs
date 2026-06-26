using System;
using System.Collections.Generic;
using NHibernaut.Mcp.Configuration;
using Xunit;

namespace NHibernaut.Mcp.Tests.Configuration;

public sealed class NHibernautMcpOptionsProviderTests
{
    [Fact]
    public void Defaults_to_loopback_dashboard_url()
    {
        var options = Resolve();

        Assert.Equal("http://127.0.0.1:5005", options.DashboardUrl);
        Assert.Null(options.DashboardToken);
        Assert.Equal(10000, options.TimeoutMs);
        Assert.Equal(25000, options.MaxOutputChars);
        Assert.False(options.AllowSensitiveOutput);
    }

    [Fact]
    public void Cli_url_overrides_environment_url()
    {
        var options = Resolve(["--url", "http://127.0.0.1:6000"], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_DASHBOARD_URL"] = "http://127.0.0.1:7000",
        });

        Assert.Equal("http://127.0.0.1:6000", options.DashboardUrl);
    }

    [Fact]
    public void Cli_token_overrides_dashboard_token_environment()
    {
        var options = Resolve(["--token", "cli-token"], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_DASHBOARD_TOKEN"] = "env-token",
        });

        Assert.Equal("cli-token", options.DashboardToken);
    }

    [Fact]
    public void Auth_token_environment_is_fallback_only()
    {
        var dashboardToken = Resolve([], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_DASHBOARD_TOKEN"] = "dashboard-token",
            ["NHIBERNAUT_AUTH_TOKEN"] = "fallback-token",
        });
        var fallbackToken = Resolve([], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_AUTH_TOKEN"] = "fallback-token",
        });

        Assert.Equal("dashboard-token", dashboardToken.DashboardToken);
        Assert.Equal("fallback-token", fallbackToken.DashboardToken);
    }

    [Fact]
    public void Sensitive_output_requires_explicit_environment_flag()
    {
        var disabledByDefault = Resolve();
        var disabledForOtherValues = Resolve([], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_MCP_ALLOW_SENSITIVE"] = "true",
        });
        var enabled = Resolve([], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_MCP_ALLOW_SENSITIVE"] = "1",
        });

        Assert.False(disabledByDefault.AllowSensitiveOutput);
        Assert.False(disabledForOtherValues.AllowSensitiveOutput);
        Assert.True(enabled.AllowSensitiveOutput);
    }

    [Fact]
    public void Invalid_timeout_uses_default_10000()
    {
        var options = Resolve(["--timeout-ms", "not-a-number"], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_MCP_TIMEOUT_MS"] = "-1",
        });

        Assert.Equal(10000, options.TimeoutMs);
    }

    [Fact]
    public void Invalid_max_output_chars_uses_default_25000()
    {
        var options = Resolve(["--max-output-chars", "0"], new Dictionary<string, string?>
        {
            ["NHIBERNAUT_MCP_MAX_OUTPUT_CHARS"] = "not-a-number",
        });

        Assert.Equal(25000, options.MaxOutputChars);
    }

    [Fact]
    public void ToString_does_not_render_dashboard_token()
    {
        var options = new NHibernautMcpOptions
        {
            DashboardUrl = "http://127.0.0.1:5005",
            DashboardToken = "sensitive-token",
        };

        var text = options.ToString();

        Assert.DoesNotContain("sensitive-token", text, StringComparison.Ordinal);
        Assert.Contains("DashboardToken=[redacted]", text, StringComparison.Ordinal);
    }

    private static NHibernautMcpOptions Resolve(
        string[]? args = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        return NHibernautMcpOptionsProvider.Resolve(
            args ?? [],
            name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);
    }
}
