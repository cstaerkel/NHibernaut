using System.Text.Json;
using NHibernaut.Mcp.Formatting;
using Xunit;

namespace NHibernaut.Mcp.Tests.Formatting;

public sealed class OutputLimiterTests
{
    [Fact]
    public void Short_output_is_not_truncated()
    {
        var limiter = new OutputLimiter(25);

        var result = limiter.LimitMarkdown("short");

        Assert.False(result.Truncated);
        Assert.Equal("short", result.Text);
    }

    [Fact]
    public void Long_markdown_output_is_truncated_with_follow_up_hint()
    {
        var limiter = new OutputLimiter(10);

        var result = limiter.LimitMarkdown("0123456789abcdef");

        Assert.True(result.Truncated);
        Assert.Contains("Call the same tool", result.Text);
        Assert.Contains("truncated: true", result.Text);
    }

    [Fact]
    public void Long_json_output_marks_truncated_true()
    {
        var limiter = new OutputLimiter(25);

        var result = limiter.LimitJson(new { value = new string('a', 100) });
        using var doc = JsonDocument.Parse(result.Text);

        Assert.True(result.Truncated);
        Assert.True(doc.RootElement.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public void Limiter_never_splits_inside_a_surrogate_pair()
    {
        var limiter = new OutputLimiter(3);

        var result = limiter.LimitMarkdown("ab\ud83d\ude42cd");

        Assert.DoesNotContain('\ud83d', result.Text);
        Assert.DoesNotContain('\ude42', result.Text);
    }
}
