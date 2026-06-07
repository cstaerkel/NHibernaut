using System.Text.RegularExpressions;

namespace NHibernaut.Core.Analysis;

/// <summary>
/// Reduces SQL to a canonical "shape" by replacing literals and parameter placeholders with a token
/// and collapsing whitespace, so statements that differ only in values group together (N+1 /
/// duplicate detection). Dialect-agnostic and regex-based — sufficient for v1.
/// </summary>
public static class SqlNormalizer
{
    private static readonly Regex StringLiteral = new("'(?:[^']|'')*'", RegexOptions.Compiled);
    private static readonly Regex Parameter = new(@"[@:?]\w+", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Normalize(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;

        var s = StringLiteral.Replace(sql, "?"); // 'literal' -> ?
        s = Parameter.Replace(s, "?");           // @p0 / :p0 / ?p0 -> ?
        s = Number.Replace(s, "?");              // 123 / 1.5 -> ? (identifiers like col1 are untouched)
        s = Whitespace.Replace(s, " ").Trim();
        return s;
    }
}
