using System.Collections.Generic;
using System.Linq;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;

namespace NHibernaut.Mcp.Formatting;

public sealed record SensitiveDataRequest
{
    public static SensitiveDataRequest Default { get; } = new();

    public SensitiveDataRequest(
        bool includeSql = true,
        bool includeParameters = false,
        bool includeStackTraces = false)
    {
        IncludeSql = includeSql;
        IncludeParameters = includeParameters;
        IncludeStackTraces = includeStackTraces;
    }

    public bool IncludeSql { get; init; }
    public bool IncludeParameters { get; init; }
    public bool IncludeStackTraces { get; init; }
}

public sealed class SensitiveDataPolicy
{
    private readonly NHibernautMcpOptions _options;

    public SensitiveDataPolicy(NHibernautMcpOptions options)
    {
        _options = options;
    }

    public McpStatement FilterStatement(McpStatement statement, SensitiveDataRequest request)
    {
        return statement with
        {
            Sql = request.IncludeSql ? statement.Sql : null,
            StackTrace = request.IncludeStackTraces && _options.AllowSensitiveOutput ? statement.StackTrace : null,
            Parameters = FilterParameters(statement.Parameters, request),
        };
    }

    public SessionDetailResult FilterSessionDetail(SessionDetailResult detail, SensitiveDataRequest request)
        => detail with { Statements = detail.Statements.Select(s => FilterStatement(s, request)).ToList() };

    public StatementResult FilterStatementResult(StatementResult result, SensitiveDataRequest request)
        => result with { Statement = FilterStatement(result.Statement, request) };

    public string RedactToken(string? token) => "[redacted]";

    private IReadOnlyList<McpParameter> FilterParameters(IReadOnlyList<McpParameter> parameters, SensitiveDataRequest request)
    {
        if (request.IncludeParameters && _options.AllowSensitiveOutput)
            return parameters;

        var reason = request.IncludeParameters
            ? "value hidden; set NHIBERNAUT_MCP_ALLOW_SENSITIVE=1 to return parameter values"
            : "value hidden; request include_parameters=true to return parameter values";

        return parameters
            .Select(p => p with { Value = null, Direction = $"{p.Direction}; {reason}" })
            .ToList();
    }
}
