using System;
using System.Globalization;
using System.Linq;
using System.Text;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Contracts;

namespace NHibernaut.Mcp.Formatting;

public sealed class McpResponseFormatter
{
    private readonly SensitiveDataPolicy _sensitiveDataPolicy;
    private readonly OutputLimiter _limiter;

    public McpResponseFormatter(NHibernautMcpOptions options)
    {
        _sensitiveDataPolicy = new SensitiveDataPolicy(options);
        _limiter = new OutputLimiter(options.MaxOutputChars);
    }

    public string Format(object result, McpResponseFormat format, SensitiveDataRequest? sensitiveDataRequest = null)
        => result switch
        {
            SessionListResult value => FormatSessionList(value, format),
            SessionDetailResult value => FormatSessionDetail(value, format, sensitiveDataRequest ?? SensitiveDataRequest.Default),
            AlertListResult value => FormatAlertList(value, format),
            QueryShapeRankResult value => FormatQueryShapes(value, format),
            CompareSessionsResult value => FormatCompare(value, format),
            StatementResult value => FormatStatement(value, format, sensitiveDataRequest ?? SensitiveDataRequest.Default),
            DiagnosticSummaryResult value => FormatDiagnosticSummary(value, format),
            _ => format == McpResponseFormat.Json
                ? _limiter.LimitJson(result).Text
                : _limiter.LimitMarkdown(Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty).Text,
        };

    public string FormatSessionList(SessionListResult result, McpResponseFormat format)
    {
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(result).Text;

        var sb = new StringBuilder();
        sb.AppendLine("# NHibernaut Sessions");
        foreach (var session in result.Sessions)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- `{session.Id}`: {session.StatementCount} statements, {session.TotalDurationMs:0.##} ms DB time, rows {session.TotalRowsRead}, writes {session.WriteCount}, alerts {session.AlertCount}, max severity {session.MaxSeverity ?? "none"}");
        }
        sb.AppendLine();
        sb.AppendLine("Next: call `nhibernaut_get_session` with a session id to inspect statements and alerts.");
        AppendPage(sb, result.Page);
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatSessionDetail(SessionDetailResult result, McpResponseFormat format, SensitiveDataRequest request)
    {
        var filtered = _sensitiveDataPolicy.FilterSessionDetail(result, request);
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(filtered).Text;

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"# Session `{filtered.Summary.Id}`: {filtered.Summary.StatementCount} statements, {filtered.Summary.TotalDurationMs:0.##} ms DB time");
        if (filtered.Alerts.Count > 0)
        {
            sb.AppendLine("## Alerts");
            foreach (var alert in filtered.Alerts)
                sb.AppendLine($"- {alert.Severity} {alert.Type} `{alert.Id}`: {alert.Title}; statements {JoinIds(alert.RelatedStatementIds)}");
        }

        sb.AppendLine("## Statements");
        foreach (var statement in filtered.Statements)
        {
            var sql = statement.Sql is null ? "SQL hidden" : statement.Sql;
            var parameters = statement.Parameters.Count == 0
                ? "no parameters"
                : statement.Parameters.Any(p => p.Value is not null)
                    ? "parameters included"
                    : "parameters hidden";
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- `{statement.Id}` {statement.Kind} {statement.DurationMs:0.##} ms rows={statement.RowsRead?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} {parameters}; shape `{statement.NormalizedSql ?? "n/a"}`; sql: {sql}");
        }

        sb.AppendLine();
        sb.AppendLine("Next: call `nhibernaut_get_statement` with a statement id for focused evidence.");
        AppendPage(sb, filtered.Page);
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatAlertList(AlertListResult result, McpResponseFormat format)
    {
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(result).Text;

        var sb = new StringBuilder();
        sb.AppendLine("# NHibernaut Alerts");
        foreach (var item in result.Alerts)
        {
            sb.AppendLine($"- session `{item.SessionId}` {item.Alert.Severity} {item.Alert.Type} `{item.Alert.Id}`: {item.Alert.Title}; statements {JoinIds(item.Alert.RelatedStatementIds)}");
        }
        sb.AppendLine();
        sb.AppendLine("Next: call `nhibernaut_get_statement` for any related statement id.");
        AppendPage(sb, result.Page);
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatQueryShapes(QueryShapeRankResult result, McpResponseFormat format)
    {
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(result).Text;

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Query Shapes sorted by {result.SortBy}");
        foreach (var shape in result.Shapes)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"- `{shape.NormalizedSql}` count={shape.ExecutionCount}, total={shape.TotalDurationMs:0.##} ms, avg={shape.AvgDurationMs:0.##} ms, maxRows={shape.MaxRowsRead}, sessions={shape.SessionCount}, nPlusOne={shape.NPlusOneIncidence}");
        }
        AppendPage(sb, result.Page);
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatCompare(CompareSessionsResult result, McpResponseFormat format)
    {
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(result).Text;

        var sb = new StringBuilder();
        sb.AppendLine($"# Compare `{result.SessionAId}` -> `{result.SessionBId}`");
        foreach (var delta in result.Deltas)
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {delta.Name}: {delta.SessionAValue:0.##} -> {delta.SessionBValue:0.##} ({FormatDelta(delta.Delta)})");

        if (result.QueryShapeDiffs.Count > 0)
        {
            sb.AppendLine("## Query Shape Count Changes");
            foreach (var diff in result.QueryShapeDiffs)
                sb.AppendLine(CultureInfo.InvariantCulture, $"- `{diff.NormalizedSql}`: {diff.SessionACount} -> {diff.SessionBCount} ({FormatDelta(diff.Delta)})");
        }

        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatStatement(StatementResult result, McpResponseFormat format, SensitiveDataRequest request)
    {
        var filtered = _sensitiveDataPolicy.FilterStatementResult(result, request);
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(filtered).Text;

        var statement = filtered.Statement;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Statement `{statement.Id}` {statement.Kind} {statement.DurationMs:0.##} ms");
        sb.AppendLine($"Session: `{statement.SessionId}`");
        sb.AppendLine($"Shape: `{statement.NormalizedSql ?? "n/a"}`");
        sb.AppendLine($"SQL: {statement.Sql ?? "SQL hidden"}");
        if (statement.Parameters.Count > 0)
        {
            sb.AppendLine("Parameters:");
            foreach (var parameter in statement.Parameters)
                sb.AppendLine($"- {parameter.Name ?? "(unnamed)"} {parameter.DbType}: {parameter.Value ?? "(hidden)"}");
        }
        if (statement.StackTrace is not null)
            sb.AppendLine($"Stack trace: {statement.StackTrace}");
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    public string FormatDiagnosticSummary(DiagnosticSummaryResult result, McpResponseFormat format)
    {
        if (format == McpResponseFormat.Json)
            return _limiter.LimitJson(result).Text;

        var sb = new StringBuilder();
        sb.AppendLine($"# Diagnostic Summary `{result.SessionId}`");
        sb.AppendLine(result.Summary);
        foreach (var finding in result.Findings)
            sb.AppendLine($"- {finding.Severity} {finding.Category}: {finding.Evidence}; statements {JoinIds(finding.StatementIds)}; suggestion: {finding.Suggestion ?? "none"}");
        if (result.NextCalls.Count > 0)
            sb.AppendLine("Next: " + string.Join("; ", result.NextCalls));
        return _limiter.LimitMarkdown(sb.ToString()).Text;
    }

    private static string JoinIds(System.Collections.Generic.IReadOnlyList<string> ids)
        => ids.Count == 0 ? "none" : string.Join(", ", ids.Select(id => $"`{id}`"));

    private static string FormatDelta(double delta)
        => delta > 0
            ? $"+{delta:0.##}"
            : delta.ToString("0.##", CultureInfo.InvariantCulture);

    private static void AppendPage(StringBuilder sb, ToolPage page)
    {
        if (!page.Truncated)
            return;

        sb.AppendLine();
        sb.AppendLine("truncated: true");
        if (!string.IsNullOrWhiteSpace(page.ContinuationHint))
            sb.AppendLine(page.ContinuationHint);
    }
}
