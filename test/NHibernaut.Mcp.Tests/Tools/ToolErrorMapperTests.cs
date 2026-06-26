using System;
using System.Net;
using System.Net.Http;
using NHibernaut.Mcp.Tools;
using Xunit;

namespace NHibernaut.Mcp.Tests.Tools;

public sealed class ToolErrorMapperTests
{
    [Fact]
    public void Invalid_guid_message_includes_example_shape()
    {
        var message = ToolErrorMapper.InvalidGuid("session_id", "not-a-guid");

        Assert.Contains("session_id", message, StringComparison.Ordinal);
        Assert.Contains("00000000-0000-0000-0000-000000000000", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unauthorized_message_mentions_token_environment_without_echoing_token()
    {
        var exception = new HttpRequestException("Unauthorized with token secret-token", null, HttpStatusCode.Unauthorized);

        var message = ToolErrorMapper.Map(exception, "http://127.0.0.1:5005");

        Assert.Contains("NHIBERNAUT_DASHBOARD_TOKEN", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_failure_message_mentions_dashboard_url_and_start_options()
    {
        var exception = new HttpRequestException("connection refused");

        var message = ToolErrorMapper.Map(exception, "http://127.0.0.1:5005");

        Assert.Contains("http://127.0.0.1:5005", message, StringComparison.Ordinal);
        Assert.Contains("NHibernautServer", message, StringComparison.Ordinal);
        Assert.Contains("standalone service", message, StringComparison.Ordinal);
        Assert.Contains("desktop embedded collector", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unexpected_exception_message_hides_internal_details()
    {
        var exception = new InvalidOperationException("database password was secret");

        var message = ToolErrorMapper.Map(exception, "http://127.0.0.1:5005");

        Assert.DoesNotContain("database password", message, StringComparison.Ordinal);
        Assert.Contains("unexpected error", message, StringComparison.OrdinalIgnoreCase);
    }
}
