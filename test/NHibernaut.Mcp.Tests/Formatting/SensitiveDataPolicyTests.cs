using System;
using System.Collections.Generic;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Formatting;
using Xunit;

namespace NHibernaut.Mcp.Tests.Formatting;

public sealed class SensitiveDataPolicyTests
{
    [Fact]
    public void Parameters_are_hidden_without_process_sensitive_gate()
    {
        var policy = new SensitiveDataPolicy(new NHibernautMcpOptions { AllowSensitiveOutput = false });

        var filtered = policy.FilterStatement(Statement(), new SensitiveDataRequest(includeParameters: true));

        Assert.Null(filtered.Parameters[0].Value);
        Assert.Contains("NHIBERNAUT_MCP_ALLOW_SENSITIVE=1", filtered.Parameters[0].Direction, StringComparison.Ordinal);
    }

    [Fact]
    public void Stack_traces_are_hidden_without_process_sensitive_gate()
    {
        var policy = new SensitiveDataPolicy(new NHibernautMcpOptions { AllowSensitiveOutput = false });

        var filtered = policy.FilterStatement(Statement(), new SensitiveDataRequest(includeStackTraces: true));

        Assert.Null(filtered.StackTrace);
    }

    [Fact]
    public void Parameters_are_returned_when_argument_and_process_gate_allow()
    {
        var policy = new SensitiveDataPolicy(new NHibernautMcpOptions { AllowSensitiveOutput = true });

        var filtered = policy.FilterStatement(Statement(), new SensitiveDataRequest(includeParameters: true));

        Assert.Equal("42", filtered.Parameters[0].Value);
    }

    [Fact]
    public void Sql_text_is_allowed_by_default_but_can_be_suppressed()
    {
        var policy = new SensitiveDataPolicy(new NHibernautMcpOptions());

        var defaultFiltered = policy.FilterStatement(Statement(), SensitiveDataRequest.Default);
        var suppressed = policy.FilterStatement(Statement(), new SensitiveDataRequest(includeSql: false));

        Assert.Equal("SELECT * FROM widgets WHERE id = @p0", defaultFiltered.Sql);
        Assert.Null(suppressed.Sql);
    }

    [Fact]
    public void Token_values_are_never_rendered()
    {
        var policy = new SensitiveDataPolicy(new NHibernautMcpOptions());

        var redacted = policy.RedactToken("token-123");

        Assert.DoesNotContain("token-123", redacted, StringComparison.Ordinal);
        Assert.Equal("[redacted]", redacted);
    }

    private static McpStatement Statement()
        => new(
            Guid.NewGuid().ToString(),
            Guid.NewGuid().ToString(),
            "SELECT * FROM widgets WHERE id = @p0",
            "SELECT * FROM widgets WHERE id = ?",
            "Select",
            DateTimeOffset.UtcNow,
            12.5,
            null,
            1,
            null,
            "at App.Repository.LoadWidget()",
            0,
            0,
            new List<McpParameter> { new("p0", "Int32", "42", 4, "Input") });
}
