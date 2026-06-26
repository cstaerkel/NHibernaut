using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using NHibernaut.Core.Model;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;

namespace NHibernaut.Mcp.Tools;

[McpServerToolType]
public sealed class NHibernautMcpTools
{
    private readonly IProfilerQueryService _queryService;
    private readonly McpResponseFormatter _formatter;
    private readonly NHibernautMcpOptions _options;

    public NHibernautMcpTools(
        IProfilerQueryService queryService,
        McpResponseFormatter formatter,
        NHibernautMcpOptions options)
    {
        _queryService = queryService;
        _formatter = formatter;
        _options = options;
    }

    [McpServerTool(Name = "nhibernaut_list_sessions", Title = "List NHibernaut Sessions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List recent NHibernaut profiler sessions so an AI client can choose a session to inspect. Defaults to 20 sessions and clamps take to 100.")]
    public async Task<string> ListSessionsAsync(
        [Description("Maximum sessions to return. Default 20, maximum 100.")] int take = 20,
        [Description("Optional ISO-8601 lower bound for session start time.")] string? since = null,
        [Description("Optional minimum alert severity: info, warning, or error.")] string? min_severity = null,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _queryService.ListSessionsAsync(
                Clamp(take, 1, 100),
                ParseDate(since),
                ParseSeverity(min_severity),
                cancellationToken).ConfigureAwait(false);
            return _formatter.FormatSessionList(result, ParseFormat(response_format));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_get_session", Title = "Get NHibernaut Session", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get one NHibernaut session summary, alerts, and bounded statements. SQL is included by default; parameter values and stack traces require explicit flags plus NHIBERNAUT_MCP_ALLOW_SENSITIVE=1.")]
    public async Task<string> GetSessionAsync(
        [Description("Session GUID returned by nhibernaut_list_sessions.")] string session_id,
        [Description("Include SQL text. Default true; set false if SQL text is sensitive.")] bool include_sql = true,
        [Description("Include parameter values if NHIBERNAUT_MCP_ALLOW_SENSITIVE=1 is also set.")] bool include_parameters = false,
        [Description("Include stack traces if NHIBERNAUT_MCP_ALLOW_SENSITIVE=1 is also set.")] bool include_stack_traces = false,
        [Description("Maximum statements to return. Default 50, maximum 100.")] int max_statements = 50,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!TryGuid(session_id, out var id))
            return ToolErrorMapper.InvalidGuid("session_id", session_id);

        try
        {
            var result = await _queryService.GetSessionAsync(id, Clamp(max_statements, 1, 100), cancellationToken).ConfigureAwait(false);
            return result is null
                ? $"No NHibernaut session found for `{id}`."
                : _formatter.FormatSessionDetail(result, ParseFormat(response_format), new SensitiveDataRequest(include_sql, include_parameters, include_stack_traces));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_list_alerts", Title = "List NHibernaut Alerts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List NHibernaut alerts by severity, type, or session. Filtering is applied before take.")]
    public async Task<string> ListAlertsAsync(
        [Description("Maximum alerts to return. Default 50, maximum 200.")] int take = 50,
        [Description("Optional minimum severity: info, warning, or error.")] string? min_severity = null,
        [Description("Optional alert type, such as SelectNPlusOne or SlowQuery.")] string? alert_type = null,
        [Description("Optional session GUID. When provided, reads alerts from that session detail.")] string? session_id = null,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(session_id) && !TryGuid(session_id, out _))
            return ToolErrorMapper.InvalidGuid("session_id", session_id);

        try
        {
            Guid? id = string.IsNullOrWhiteSpace(session_id) ? null : Guid.Parse(session_id);
            var result = await _queryService.ListAlertsAsync(
                Clamp(take, 1, 200),
                ParseSeverity(min_severity),
                alert_type,
                id,
                cancellationToken).ConfigureAwait(false);
            return _formatter.FormatAlertList(result, ParseFormat(response_format));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_rank_query_shapes", Title = "Rank NHibernaut Query Shapes", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Rank normalized SQL query shapes by total duration, average duration, execution count, or N+1 incidence.")]
    public async Task<string> RankQueryShapesAsync(
        [Description("Maximum query shapes to return. Default 20, maximum 100.")] int limit = 20,
        [Description("Sort key: total_duration_ms, avg_duration_ms, execution_count, or n_plus_one_incidence.")] string sort_by = "total_duration_ms",
        [Description("Minimum execution count for a shape to be included.")] int min_execution_count = 1,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _queryService.RankQueryShapesAsync(
                Clamp(limit, 1, 100),
                ParseSortBy(sort_by),
                Math.Max(1, min_execution_count),
                cancellationToken).ConfigureAwait(false);
            return _formatter.FormatQueryShapes(result, ParseFormat(response_format));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_compare_sessions", Title = "Compare NHibernaut Sessions", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Compare two NHibernaut sessions for before/after validation using per-session details, including normalized SQL shape count changes.")]
    public async Task<string> CompareSessionsAsync(
        [Description("Baseline session GUID.")] string session_a_id,
        [Description("Comparison session GUID.")] string session_b_id,
        [Description("Include normalized SQL shape diffs. Default true.")] bool include_query_shape_diffs = true,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!TryGuid(session_a_id, out var a))
            return ToolErrorMapper.InvalidGuid("session_a_id", session_a_id);
        if (!TryGuid(session_b_id, out var b))
            return ToolErrorMapper.InvalidGuid("session_b_id", session_b_id);

        try
        {
            var result = await _queryService.CompareSessionsAsync(a, b, include_query_shape_diffs, cancellationToken).ConfigureAwait(false);
            return result is null
                ? $"One or both NHibernaut sessions were not found: `{a}`, `{b}`."
                : _formatter.FormatCompare(result, ParseFormat(response_format));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_get_statement", Title = "Get NHibernaut Statement", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get one statement and related entities/writes/collection initializations. Parameter values and stack trace require explicit flags plus NHIBERNAUT_MCP_ALLOW_SENSITIVE=1.")]
    public async Task<string> GetStatementAsync(
        [Description("Session GUID returned by nhibernaut_list_sessions.")] string session_id,
        [Description("Statement GUID returned by nhibernaut_get_session or nhibernaut_list_alerts.")] string statement_id,
        [Description("Include parameter values if NHIBERNAUT_MCP_ALLOW_SENSITIVE=1 is also set.")] bool include_parameters = false,
        [Description("Include stack trace if NHIBERNAUT_MCP_ALLOW_SENSITIVE=1 is also set.")] bool include_stack_trace = false,
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!TryGuid(session_id, out var sessionId))
            return ToolErrorMapper.InvalidGuid("session_id", session_id);
        if (!TryGuid(statement_id, out var statementId))
            return ToolErrorMapper.InvalidGuid("statement_id", statement_id);

        try
        {
            var result = await _queryService.GetStatementAsync(sessionId, statementId, cancellationToken).ConfigureAwait(false);
            return result is null
                ? $"No NHibernaut statement `{statementId}` found in session `{sessionId}`."
                : _formatter.FormatStatement(result, ParseFormat(response_format), new SensitiveDataRequest(includeParameters: include_parameters, includeStackTraces: include_stack_trace));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    [McpServerTool(Name = "nhibernaut_summarize_session", Title = "Summarize NHibernaut Session", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarize a NHibernaut session with evidence and next MCP calls. Focus can be alerts, n_plus_one, writes, slow_queries, or all.")]
    public async Task<string> SummarizeSessionAsync(
        [Description("Session GUID returned by nhibernaut_list_sessions.")] string session_id,
        [Description("Focus: alerts, n_plus_one, writes, slow_queries, or all.")] string focus = "all",
        [Description("Response format: markdown or json.")] string response_format = "markdown",
        CancellationToken cancellationToken = default)
    {
        if (!TryGuid(session_id, out var sessionId))
            return ToolErrorMapper.InvalidGuid("session_id", session_id);

        try
        {
            var result = await _queryService.SummarizeSessionAsync(sessionId, ParseFocus(focus), cancellationToken).ConfigureAwait(false);
            return result is null
                ? $"No NHibernaut session found for `{sessionId}`."
                : _formatter.FormatDiagnosticSummary(result, ParseFormat(response_format));
        }
        catch (Exception ex)
        {
            return ToolErrorMapper.Map(ex, _options.DashboardUrl);
        }
    }

    private static bool TryGuid(string? value, out Guid id)
        => Guid.TryParse(value, out id);

    private static int Clamp(int value, int min, int max)
        => Math.Min(Math.Max(value, min), max);

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var date) ? date : null;

    private static AlertSeverity? ParseSeverity(string? value)
        => Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var severity) ? severity : null;

    private static McpResponseFormat ParseFormat(string? value)
        => string.Equals(value, "json", StringComparison.OrdinalIgnoreCase) ? McpResponseFormat.Json : McpResponseFormat.Markdown;

    private static QueryShapeSortBy ParseSortBy(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "avg_duration_ms" => QueryShapeSortBy.AvgDurationMs,
            "execution_count" => QueryShapeSortBy.ExecutionCount,
            "n_plus_one_incidence" => QueryShapeSortBy.NPlusOneIncidence,
            _ => QueryShapeSortBy.TotalDurationMs,
        };

    private static DiagnosticFocus ParseFocus(string? value)
        => (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "alerts" => DiagnosticFocus.Alerts,
            "n_plus_one" => DiagnosticFocus.NPlusOne,
            "writes" => DiagnosticFocus.Writes,
            "slow_queries" => DiagnosticFocus.SlowQueries,
            _ => DiagnosticFocus.All,
        };
}
