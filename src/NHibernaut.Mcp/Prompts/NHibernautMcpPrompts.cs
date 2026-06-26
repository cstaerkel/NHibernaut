using System.ComponentModel;
using ModelContextProtocol.Server;

namespace NHibernaut.Mcp.Prompts;

[McpServerPromptType]
public sealed class NHibernautMcpPrompts
{
    [McpServerPrompt(Name = "triage_recent_nhibernate_activity", Title = "Triage Recent NHibernate Activity")]
    [Description("Guide an AI client through recent NHibernaut sessions and the highest-severity issues.")]
    public string TriageRecentNHibernateActivity()
        => """
           Triage recent NHibernate profiler activity with NHibernaut.

           1. Call nhibernaut_list_sessions with the default markdown response.
           2. Choose the highest-severity or most expensive sessions first.
           3. For each chosen session, call nhibernaut_get_session with include_parameters=false and include_stack_traces=false unless the user explicitly asks for sensitive evidence.
           4. Use nhibernaut_summarize_session when you need a concise evidence summary.
           5. Report only evidence returned by tools: session ids, alert types and titles, statement ids, query counts, row counts, DB time, and suggested next tool calls.
           6. Separate confirmed findings from hypotheses and recommend the smallest next inspection step.
           """;

    [McpServerPrompt(Name = "explain_session_alerts", Title = "Explain NHibernaut Session Alerts")]
    [Description("Guide an AI client through the alerts and related statements for one NHibernaut session.")]
    public string ExplainSessionAlerts(
        [Description("Session GUID returned by nhibernaut_list_sessions.")] string session_id)
        => $"""
            Explain the NHibernaut alerts for session {session_id}.

            1. Call nhibernaut_list_alerts with session_id="{session_id}" to get the alert list for this session.
            2. Call nhibernaut_get_session with session_id="{session_id}" to see the bounded statement context.
            3. For each alert with related statement ids, call nhibernaut_get_statement for the most relevant statements.
            4. Use nhibernaut_summarize_session with focus="alerts" if the alert set needs a short evidence summary.
            5. Explain each alert in plain language, cite the exact statement ids and query shapes returned by the tools, and include remediation ideas only when the tool output supports them.
            6. Do not request parameter values or stack traces unless the user explicitly asks and the process-level sensitive-output gate is enabled.
            """;

    [McpServerPrompt(Name = "compare_nhibernate_sessions", Title = "Compare NHibernate Sessions")]
    [Description("Guide before/after NHibernate session comparison with evidence from NHibernaut.")]
    public string CompareNHibernateSessions(
        [Description("Baseline session GUID.")] string session_a_id,
        [Description("Comparison session GUID.")] string session_b_id)
        => $"""
            Compare NHibernaut session {session_a_id} against session {session_b_id}.

            1. Call nhibernaut_compare_sessions with session_a_id="{session_a_id}" and session_b_id="{session_b_id}".
            2. If a delta needs explanation, call nhibernaut_get_session for each session and inspect the statements that changed.
            3. Use query-shape diffs to distinguish real ORM behavior changes from noise.
            4. Report whether the comparison looks like an improvement, regression, or inconclusive result based on DB time, statement count, rows read, writes, alerts, and normalized SQL shape counts.
            5. Quote session ids and numeric deltas from tool output, and call out missing evidence instead of filling gaps.
            """;

    [McpServerPrompt(Name = "investigate_n_plus_one", Title = "Investigate NHibernate N+1")]
    [Description("Guide N+1 investigation across session alerts, statements, and aggregate query shapes.")]
    public string InvestigateNPlusOne(
        [Description("Session GUID returned by nhibernaut_list_sessions.")] string session_id)
        => $"""
            Investigate possible NHibernate N+1 behavior in session {session_id}.

            1. Call nhibernaut_summarize_session with session_id="{session_id}" and focus="n_plus_one".
            2. Call nhibernaut_get_session with session_id="{session_id}" and inspect repeated normalized SQL shapes plus SelectNPlusOne alerts.
            3. Call nhibernaut_rank_query_shapes with sort_by="n_plus_one_incidence" to see whether the same shape is systemic across sessions.
            4. For related statement ids, call nhibernaut_get_statement and inspect entity loads, collection initializations, rows read, and query shape.
            5. Report evidence for the N+1 pattern, likely mapping or query cause, and practical fixes such as fetch joins, batch fetching, futures, projections, or explicit preloading when supported by the returned evidence.
            6. Avoid sensitive parameter values and stack traces unless the user explicitly requests them and the server is configured to allow sensitive output.
            """;
}
