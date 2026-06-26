using System;
using NHibernaut.Mcp.Prompts;
using Xunit;

namespace NHibernaut.Mcp.Tests.Prompts;

public sealed class NHibernautMcpPromptsTests
{
    [Fact]
    public void Recent_triage_prompt_references_list_sessions_then_get_session()
    {
        var prompts = new NHibernautMcpPrompts();

        var text = prompts.TriageRecentNHibernateActivity();

        Assert.Contains("nhibernaut_list_sessions", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_get_session", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("nhibernaut_list_sessions", StringComparison.Ordinal) <
            text.IndexOf("nhibernaut_get_session", StringComparison.Ordinal));
    }

    [Fact]
    public void Explain_alerts_prompt_accepts_session_id()
    {
        var prompts = new NHibernautMcpPrompts();

        var text = prompts.ExplainSessionAlerts("session-under-review");

        Assert.Contains("session-under-review", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_list_alerts", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_get_statement", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_prompt_accepts_two_session_ids()
    {
        var prompts = new NHibernautMcpPrompts();

        var text = prompts.CompareNHibernateSessions("baseline-session", "candidate-session");

        Assert.Contains("baseline-session", text, StringComparison.Ordinal);
        Assert.Contains("candidate-session", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_compare_sessions", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NPlusOne_prompt_guides_statement_and_shape_inspection()
    {
        var prompts = new NHibernautMcpPrompts();

        var text = prompts.InvestigateNPlusOne("target-session");

        Assert.Contains("nhibernaut_summarize_session", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_rank_query_shapes", text, StringComparison.Ordinal);
        Assert.Contains("nhibernaut_get_statement", text, StringComparison.Ordinal);
        Assert.Contains("N+1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prompts_do_not_embed_stale_session_data()
    {
        var prompts = new NHibernautMcpPrompts();
        var combined = string.Join(
            Environment.NewLine,
            prompts.TriageRecentNHibernateActivity(),
            prompts.ExplainSessionAlerts("target-session"),
            prompts.CompareNHibernateSessions("baseline-session", "candidate-session"),
            prompts.InvestigateNPlusOne("target-session"));

        Assert.DoesNotContain("11111111-1111-1111-1111-111111111111", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT * FROM", combined, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opaque-token-value", combined, StringComparison.Ordinal);
    }
}
