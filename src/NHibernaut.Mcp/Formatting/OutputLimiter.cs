using System;
using System.Text.Json;

namespace NHibernaut.Mcp.Formatting;

public sealed record LimitedOutput(string Text, bool Truncated, string? ContinuationHint);

public sealed class OutputLimiter
{
    public const string DefaultContinuationHint = "Call the same tool with a smaller limit or narrower filter.";

    private readonly int _maxChars;

    public OutputLimiter(int maxChars)
    {
        _maxChars = Math.Max(maxChars, 1);
    }

    public LimitedOutput LimitMarkdown(string text, string continuationHint = DefaultContinuationHint)
    {
        if (text.Length <= _maxChars)
            return new LimitedOutput(text, false, null);

        var prefix = SafePrefix(text, _maxChars);
        var limited = $"{prefix}\n\ntruncated: true\n{continuationHint}";
        return new LimitedOutput(limited, true, continuationHint);
    }

    public LimitedOutput LimitJson<T>(T value, string continuationHint = DefaultContinuationHint)
    {
        var json = JsonResultSerializer.Serialize(value);
        if (json.Length <= _maxChars)
            return new LimitedOutput(json, false, null);

        var truncated = JsonSerializer.Serialize(new
        {
            truncated = true,
            continuationHint,
        }, JsonResultSerializer.Options);

        return new LimitedOutput(truncated, true, continuationHint);
    }

    private static string SafePrefix(string text, int maxChars)
    {
        var length = Math.Min(maxChars, text.Length);
        if (length > 0 && char.IsHighSurrogate(text[length - 1]))
            length--;
        return text.Substring(0, length);
    }
}
