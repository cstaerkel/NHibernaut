using System;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using NHibernate.Impl;
using NHibernaut.Core.Analysis;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Mutable state for one in-flight statement: the model record, a stopwatch, the owning session id,
/// a live row counter, and the previous <see cref="CaptureContext.CurrentStatement"/> (so nested
/// statements restore — not erase — their parent's attribution window).
/// </summary>
internal sealed class StatementCapture
{
    private int _finalized;

    public StatementCapture(ProfiledStatement statement, Guid sessionId, StatementCapture? previous)
    {
        Statement = statement;
        SessionId = sessionId;
        Previous = previous;
        Stopwatch = Stopwatch.StartNew();
    }

    public ProfiledStatement Statement { get; }
    public Guid SessionId { get; }
    public StatementCapture? Previous { get; }
    public Stopwatch Stopwatch { get; }
    public int RowsRead;

    /// <summary>Ensures duration/rows are finalized exactly once (reader-close vs. fail can race).</summary>
    public bool TryFinalize() => Interlocked.Exchange(ref _finalized, 1) == 0;
}

/// <summary>Ambient capture state flowing with the async/sync execution context.</summary>
internal static class CaptureContext
{
    /// <summary>
    /// The statement currently executing / whose reader is open. Load and collection-init events
    /// firing in this window attach to it (Milestone 3).
    /// </summary>
    public static readonly AsyncLocal<StatementCapture?> CurrentStatement = new();
}

/// <summary>Derives <see cref="StatementKind"/> from a SQL string's leading keyword.</summary>
internal static class SqlKind
{
    public static StatementKind Of(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return StatementKind.Other;

        var i = 0;
        while (i < sql!.Length && char.IsWhiteSpace(sql[i])) i++;
        var rest = sql.AsSpan(i);

        if (rest.StartsWith("select", StringComparison.OrdinalIgnoreCase)) return StatementKind.Select;
        if (rest.StartsWith("insert", StringComparison.OrdinalIgnoreCase)) return StatementKind.Insert;
        if (rest.StartsWith("update", StringComparison.OrdinalIgnoreCase)) return StatementKind.Update;
        if (rest.StartsWith("delete", StringComparison.OrdinalIgnoreCase)) return StatementKind.Delete;
        return StatementKind.Other;
    }
}

/// <summary>
/// Central, fail-safe capture orchestration shared by the command and reader wrappers. Every public
/// entry point swallows exceptions so capture can never surface to (or break) the host.
/// </summary>
internal static class Capturer
{
    /// <summary>Begin capturing a statement; returns the capture or null if capture was unavailable.</summary>
    public static StatementCapture? Begin(DbCommand inner)
    {
        return NHibernautRuntime.SafeExecute<StatementCapture?>(() =>
        {
            var sql = inner.CommandText ?? string.Empty;
            var sessionId = SessionIdLoggingContext.SessionId ?? Guid.Empty;

            if (!NHibernautRuntime.IsSampled(sessionId)) return null; // sampling: skip this session

            var statement = new ProfiledStatement
            {
                Sql = sql,
                NormalizedSql = SqlNormalizer.Normalize(sql),
                SessionId = sessionId,
                StartedAt = DateTimeOffset.UtcNow,
                Kind = SqlKind.Of(sql),
                StackTrace = CaptureStack(NHibernautRuntime.Options)
            };
            SnapshotParameters(inner, statement);

            var capture = new StatementCapture(statement, sessionId, CaptureContext.CurrentStatement.Value);
            CaptureContext.CurrentStatement.Value = capture;

            var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
            lock (session.SyncRoot)
            {
                session.Statements.Add(statement);
                session.ThreadIds.Add(Environment.CurrentManagedThreadId);

                // Link to the session's currently-open connection (heuristic, see PROGRESS.md).
                for (var i = session.Connections.Count - 1; i >= 0; i--)
                {
                    if (session.Connections[i].ClosedAt is null)
                    {
                        session.Connections[i].StatementIds.Add(statement.Id);
                        break;
                    }
                }
            }
            return capture;
        }, null);
    }

    /// <summary>Finalize a non-reader statement (non-query/scalar) with its rows-affected, if any.</summary>
    public static void Complete(StatementCapture? capture, int? rowsAffected)
    {
        if (capture is null) return;
        NHibernautRuntime.SafeExecute(() =>
        {
            if (!capture.TryFinalize()) { Restore(capture); return; }
            capture.Stopwatch.Stop();
            capture.Statement.DurationMs = capture.Stopwatch.Elapsed.TotalMilliseconds;
            if (rowsAffected.HasValue) capture.Statement.RowsAffected = rowsAffected;
            Restore(capture);
        });
    }

    /// <summary>Finalize a reader-backed statement on reader close: stamp duration and rows read.</summary>
    public static void CompleteReader(StatementCapture capture)
    {
        NHibernautRuntime.SafeExecute(() =>
        {
            if (!capture.TryFinalize()) { Restore(capture); return; }
            capture.Stopwatch.Stop();
            capture.Statement.DurationMs = capture.Stopwatch.Elapsed.TotalMilliseconds;
            capture.Statement.RowsRead = capture.RowsRead;
            Restore(capture);
        });
    }

    /// <summary>Record an exception on the statement (it is rethrown unchanged by the caller).</summary>
    public static void Fail(StatementCapture? capture, Exception ex)
    {
        if (capture is null) return;
        NHibernautRuntime.SafeExecute(() =>
        {
            capture.Statement.Exception = ex.Message;
            if (capture.TryFinalize())
            {
                capture.Stopwatch.Stop();
                capture.Statement.DurationMs = capture.Stopwatch.Elapsed.TotalMilliseconds;
            }
            Restore(capture);
        });
    }

    private static void Restore(StatementCapture capture)
    {
        if (ReferenceEquals(CaptureContext.CurrentStatement.Value, capture))
            CaptureContext.CurrentStatement.Value = capture.Previous;
    }

    // Filtered stack trace for click-to-source. Off unless CaptureStackTraces (expensive). Frames in
    // the configured namespaces (NHibernate.*, System.Data.*, NHibernaut.*) are stripped so the first
    // remaining frame is the app's call site.
    private static string? CaptureStack(NHibernautOptions options)
    {
        if (!options.CaptureStackTraces) return null;

        var trace = new StackTrace(fNeedFileInfo: true);
        var sb = new StringBuilder();
        foreach (var frame in trace.GetFrames() ?? Array.Empty<StackFrame>())
        {
            var method = frame.GetMethod();
            var ns = method?.DeclaringType?.Namespace ?? string.Empty;
            if (method?.DeclaringType is null) continue;
            if (options.StackTraceNamespaceFilter.Any(p => ns.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            var file = frame.GetFileName();
            var line = frame.GetFileLineNumber();
            sb.Append(method.DeclaringType.FullName).Append('.').Append(method.Name);
            if (!string.IsNullOrEmpty(file)) sb.Append(" (").Append(file).Append(':').Append(line).Append(')');
            sb.Append('\n');
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    private static void SnapshotParameters(DbCommand inner, ProfiledStatement statement)
    {
        var options = NHibernautRuntime.Options;
        foreach (DbParameter p in inner.Parameters)
        {
            var capture = new ParamCapture
            {
                Name = p.ParameterName,
                DbType = p.DbType.ToString(),
                Size = p.Size,
                Direction = p.Direction
            };

            if (options.CaptureParameterValues)
            {
                var value = p.Value;
                if (options.ParameterRedactor is not null)
                {
                    value = options.ParameterRedactor(new ParamContext
                    {
                        ParameterName = p.ParameterName,
                        Value = value,
                        DbType = capture.DbType,
                        Sql = statement.Sql
                    });
                }
                capture.Value = value;
            }

            statement.Parameters.Add(capture);
        }
    }
}
