using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Tools;

namespace NHibernaut.Mcp.Resources;

[McpServerResourceType]
public sealed class NHibernautMcpResources
{
    public const string ConfigUri = "nhibernaut://config";
    public const string SessionsUri = "nhibernaut://sessions";
    public const string SessionUriTemplate = "nhibernaut://sessions/{session_id}";
    public const string AggregateUri = "nhibernaut://aggregate";
    public const string AlertsUri = "nhibernaut://alerts";

    private const string MarkdownMimeType = "text/markdown";
    private readonly IProfilerQueryService _queryService;
    private readonly McpResponseFormatter _formatter;
    private readonly NHibernautMcpOptions _options;

    public NHibernautMcpResources(
        IProfilerQueryService queryService,
        McpResponseFormatter formatter,
        NHibernautMcpOptions options)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    [McpServerResource(UriTemplate = ConfigUri, Name = "nhibernaut_config", Title = "NHibernaut MCP Configuration", MimeType = MarkdownMimeType)]
    [Description("Current NHibernaut MCP dashboard connection settings visible to the server, excluding credential values.")]
    public TextResourceContents Config()
        => Text(ConfigUri,
            "# NHibernaut MCP Configuration" + Environment.NewLine +
            $"- dashboard_url: {_options.DashboardUrl}" + Environment.NewLine +
            $"- authentication: {(string.IsNullOrWhiteSpace(_options.DashboardToken) ? "not configured" : "configured")}" + Environment.NewLine +
            $"- timeout_ms: {_options.TimeoutMs}" + Environment.NewLine +
            $"- max_output_chars: {_options.MaxOutputChars}" + Environment.NewLine +
            $"- allow_sensitive_output: {_options.AllowSensitiveOutput.ToString().ToLowerInvariant()}" + Environment.NewLine);

    [McpServerResource(UriTemplate = SessionsUri, Name = "nhibernaut_sessions", Title = "Recent NHibernaut Sessions", MimeType = MarkdownMimeType)]
    [Description("Bounded recent NHibernaut profiler session summaries for choosing a session to inspect.")]
    public async Task<TextResourceContents> SessionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _queryService.ListSessionsAsync(20, ct: cancellationToken).ConfigureAwait(false);
            return Text(SessionsUri, _formatter.FormatSessionList(result, McpResponseFormat.Markdown));
        }
        catch (Exception ex)
        {
            return Text(SessionsUri, ToolErrorMapper.Map(ex, _options.DashboardUrl));
        }
    }

    [McpServerResource(UriTemplate = SessionUriTemplate, Name = "nhibernaut_session_detail", Title = "NHibernaut Session Detail", MimeType = MarkdownMimeType)]
    [Description("One NHibernaut session detail with the same redaction and truncation defaults as nhibernaut_get_session.")]
    public async Task<TextResourceContents> SessionAsync(string session_id, CancellationToken cancellationToken = default)
    {
        var uri = $"nhibernaut://sessions/{session_id}";
        if (!Guid.TryParse(session_id, out var sessionId))
            return Text(uri, ToolErrorMapper.InvalidGuid("session_id", session_id));

        try
        {
            var result = await _queryService.GetSessionAsync(sessionId, 50, cancellationToken).ConfigureAwait(false);
            var text = result is null
                ? $"No NHibernaut session found for `{sessionId}`."
                : _formatter.FormatSessionDetail(result, McpResponseFormat.Markdown, SensitiveDataRequest.Default);
            return Text(uri, text);
        }
        catch (Exception ex)
        {
            return Text(uri, ToolErrorMapper.Map(ex, _options.DashboardUrl));
        }
    }

    [McpServerResource(UriTemplate = AggregateUri, Name = "nhibernaut_aggregate", Title = "NHibernaut Query Shapes", MimeType = MarkdownMimeType)]
    [Description("Bounded aggregate query-shape ranking from the current NHibernaut profiler dashboard.")]
    public async Task<TextResourceContents> AggregateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _queryService.RankQueryShapesAsync(20, QueryShapeSortBy.TotalDurationMs, 1, cancellationToken).ConfigureAwait(false);
            return Text(AggregateUri, _formatter.FormatQueryShapes(result, McpResponseFormat.Markdown));
        }
        catch (Exception ex)
        {
            return Text(AggregateUri, ToolErrorMapper.Map(ex, _options.DashboardUrl));
        }
    }

    [McpServerResource(UriTemplate = AlertsUri, Name = "nhibernaut_alerts", Title = "NHibernaut Alerts", MimeType = MarkdownMimeType)]
    [Description("Bounded recent NHibernaut alert feed for triaging profiler findings.")]
    public async Task<TextResourceContents> AlertsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _queryService.ListAlertsAsync(50, ct: cancellationToken).ConfigureAwait(false);
            return Text(AlertsUri, _formatter.FormatAlertList(result, McpResponseFormat.Markdown));
        }
        catch (Exception ex)
        {
            return Text(AlertsUri, ToolErrorMapper.Map(ex, _options.DashboardUrl));
        }
    }

    private static TextResourceContents Text(string uri, string text)
        => new()
        {
            Uri = uri,
            MimeType = MarkdownMimeType,
            Text = text,
        };
}
