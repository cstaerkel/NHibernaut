using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Analysis;

internal static class AlertFactory
{
    public static Alert Make(string type, AlertSeverity severity, string title, string description, string? suggestion, IEnumerable<Guid>? related = null)
    {
        var alert = new Alert
        {
            Type = type,
            Severity = severity,
            Title = title,
            Description = description,
            Suggestion = suggestion
        };
        if (related is not null) alert.RelatedStatementIds.AddRange(related);
        return alert;
    }
}

/// <summary>>= N statements of one query shape, or >= N collection inits of one role, in a session.</summary>
public sealed class SelectNPlusOneDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        var n = options.NPlusOneThreshold;
        var emitted = false;

        foreach (var group in session.Statements
                     .Where(s => s.Kind == StatementKind.Select && !string.IsNullOrEmpty(s.NormalizedSql))
                     .GroupBy(s => s.NormalizedSql))
        {
            var ids = group.Select(s => s.Id).ToList();
            if (ids.Count < n) continue;
            emitted = true;
            yield return AlertFactory.Make(
                "SelectNPlusOne", AlertSeverity.Warning,
                $"Select N+1: {ids.Count}× the same query shape",
                $"{ids.Count} statements share the shape: {group.Key}",
                "Consider eager fetch (Fetch/FetchMany), a batch-size mapping, or a future/multi-query.",
                ids);
        }

        if (emitted) yield break;

        // Fallback: batch-fetched collections may produce few queries but many inits of one role.
        foreach (var group in session.CollectionInits.GroupBy(c => c.Role))
        {
            var count = group.Count();
            if (count < n) continue;
            yield return AlertFactory.Make(
                "SelectNPlusOne", AlertSeverity.Warning,
                $"Select N+1: {count}× initialized collection '{group.Key}'",
                $"The lazy collection '{group.Key}' was initialized {count} times in one session.",
                "Eager-fetch the collection, set a batch-size on the mapping, or use a future/multi-query.",
                group.Where(c => c.StatementId.HasValue).Select(c => c.StatementId!.Value));
        }
    }
}

/// <summary>Session statement count above the threshold.</summary>
public sealed class TooManyQueriesDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        if (session.StatementCount <= options.TooManyQueriesThreshold) yield break;
        yield return AlertFactory.Make(
            "TooManyQueries", AlertSeverity.Warning,
            $"Too many queries: {session.StatementCount} in one session",
            $"This session executed {session.StatementCount} statements (threshold {options.TooManyQueriesThreshold}).",
            "Batch reads, use joins/eager fetch, or cache to reduce round-trips.");
    }
}

/// <summary>An unlimited SELECT returning more rows than the threshold.</summary>
public sealed class UnboundedResultSetDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        foreach (var s in session.Statements)
        {
            if (s.Kind != StatementKind.Select) continue;
            if ((s.RowsRead ?? 0) <= options.UnboundedResultSetRowThreshold) continue;
            if (HasRowLimit(s.Sql)) continue;
            yield return AlertFactory.Make(
                "UnboundedResultSet", AlertSeverity.Warning,
                $"Unbounded result set: {s.RowsRead} rows, no limit",
                $"A SELECT with no row-limiting clause returned {s.RowsRead} rows.",
                "Add paging (Take/SetMaxResults) or a WHERE filter to bound the result set.",
                new[] { s.Id });
        }
    }

    private static bool HasRowLimit(string sql)
    {
        var lower = " " + sql.ToLowerInvariant() + " ";
        return lower.Contains(" limit ") || lower.Contains(" top ") || lower.Contains(" fetch ") || lower.Contains("rownum");
    }
}

/// <summary>A single statement returning more rows than the threshold.</summary>
public sealed class TooManyRowsDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        foreach (var s in session.Statements)
        {
            if ((s.RowsRead ?? 0) <= options.TooManyRowsThreshold) continue;
            yield return AlertFactory.Make(
                "TooManyRows", AlertSeverity.Warning,
                $"Too many rows: {s.RowsRead} from one statement",
                $"A statement returned {s.RowsRead} rows (threshold {options.TooManyRowsThreshold}).",
                "Project only needed columns and page large reads.",
                new[] { s.Id });
        }
    }
}

/// <summary>JOIN count in one statement above the threshold.</summary>
public sealed class TooManyJoinsDetector : IAlertDetector
{
    private static readonly Regex JoinPattern = new(@"\bjoin\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        foreach (var s in session.Statements)
        {
            var joins = JoinPattern.Matches(s.Sql).Count;
            if (joins <= options.TooManyJoinsThreshold) continue;
            yield return AlertFactory.Make(
                "TooManyJoins", AlertSeverity.Info,
                $"Too many joins: {joins} in one statement",
                $"A statement contains {joins} JOINs (threshold {options.TooManyJoinsThreshold}).",
                "Consider splitting the query or denormalizing hot paths.",
                new[] { s.Id });
        }
    }
}

/// <summary>Statement duration above the threshold.</summary>
public sealed class SlowQueryDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        foreach (var s in session.Statements)
        {
            if (s.DurationMs <= options.SlowQueryMs) continue;
            yield return AlertFactory.Make(
                "SlowQuery", AlertSeverity.Warning,
                $"Slow query: {s.DurationMs:F0} ms",
                $"A statement took {s.DurationMs:F0} ms (threshold {options.SlowQueryMs} ms).",
                "Check indexes, reduce returned data, or cache the result.",
                new[] { s.Id });
        }
    }
}

/// <summary>Identical SQL and identical parameters executed more than once in a session.</summary>
public sealed class DuplicateQueryDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        foreach (var group in session.Statements.GroupBy(Signature))
        {
            var ids = group.Select(s => s.Id).ToList();
            if (ids.Count <= 1) continue;
            yield return AlertFactory.Make(
                "DuplicateQuery", AlertSeverity.Info,
                $"Duplicate query executed {ids.Count}×",
                $"The same SQL with the same parameters ran {ids.Count} times.",
                "Reuse the first result, or enable the session/second-level cache.",
                ids);
        }
    }

    private static string Signature(ProfiledStatement s)
        => s.Sql + "|" + string.Join(",", s.Parameters.Select(p =>
            p.Name + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)));
}

/// <summary>One session id observed on more than one managed thread.</summary>
public sealed class CrossThreadSessionDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        if (session.ThreadIds.Count <= 1) yield break;
        yield return AlertFactory.Make(
            "CrossThreadSession", AlertSeverity.Error,
            $"Session used on {session.ThreadIds.Count} threads",
            $"This session was observed on {session.ThreadIds.Count} managed threads ({string.Join(", ", session.ThreadIds)}). " +
            "NHibernate sessions are not thread-safe.",
            "Use one session per thread / unit of work; never share a session across threads.");
    }
}

/// <summary>Insert/Update/Delete statements not covered by any transaction window.</summary>
public sealed class WriteWithoutTransactionDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        var uncovered = session.Statements
            .Where(s => s.Kind is StatementKind.Insert or StatementKind.Update or StatementKind.Delete)
            .Where(s => !session.Transactions.Any(t =>
                t.BeganAt <= s.StartedAt && s.StartedAt <= (t.CompletedAt ?? DateTimeOffset.MaxValue)))
            .Select(s => s.Id)
            .ToList();

        if (uncovered.Count == 0) yield break;
        yield return AlertFactory.Make(
            "WriteWithoutTransaction", AlertSeverity.Warning,
            $"{uncovered.Count} write(s) without a transaction",
            $"{uncovered.Count} write statement(s) executed with no active transaction.",
            "Wrap writes in an explicit transaction for atomicity and performance.",
            uncovered);
    }
}

/// <summary>Session write count above the threshold.</summary>
public sealed class TooManyWritesDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        if (session.WriteCount <= options.TooManyWritesThreshold) yield break;
        yield return AlertFactory.Make(
            "TooManyWrites", AlertSeverity.Info,
            $"Too many writes: {session.WriteCount} in one session",
            $"This session flushed {session.WriteCount} writes (threshold {options.TooManyWritesThreshold}).",
            "Batch writes (adonet.batch_size) or restructure the unit of work.");
    }
}

/// <summary>Update flushed where no tracked property actually changed (best-effort).</summary>
public sealed class SuperfluousUpdateDetector : IAlertDetector
{
    public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
    {
        var count = session.Writes.Count(w => w.Kind == WriteKind.Update && w.NoActualChange);
        if (count == 0) yield break;
        yield return AlertFactory.Make(
            "SuperfluousUpdate", AlertSeverity.Info,
            $"{count} superfluous update(s)",
            $"{count} UPDATE(s) were issued where no tracked property changed.",
            "Avoid reattaching unchanged entities; check for unintended dirtying or flush timing.");
    }
}
