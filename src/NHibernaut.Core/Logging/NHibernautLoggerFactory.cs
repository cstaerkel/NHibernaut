using System;
using NHibernate;
using NHibernate.Impl;
using NHibernaut.Core.Analysis;
using NHibernaut.Core.Capture;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Logging;

/// <summary>
/// Tier B (zero-touch) logger factory. Routes the <c>NHibernate.SQL</c> logger to a capturing logger
/// and everything else to a no-op. Installed by <see cref="LoggingBaseline"/> on assembly load.
/// </summary>
/// <remarks>
/// Caveat: installing this replaces NHibernate's logger factory for the process. By default NHibernate
/// logs nothing (no-op factory), so this is transparent; if a host configured log4net/NLog for
/// NHibernate, that is overridden. Tier A (<c>EnableNHibernaut</c>) is the recommended full-fidelity path
/// and makes this baseline stand down.
/// </remarks>
internal sealed class NHibernautLoggerFactory : INHibernateLoggerFactory
{
    private static readonly SqlCapturingLogger Sql = new();
    private static readonly NoOpLogger NoOp = new();

    public INHibernateLogger LoggerFor(string keyName)
        => keyName == "NHibernate.SQL" ? Sql : NoOp;

    public INHibernateLogger LoggerFor(Type type)
        => LoggerFor(type.FullName ?? type.Name);
}

internal sealed class NoOpLogger : INHibernateLogger
{
    public bool IsEnabled(NHibernateLogLevel logLevel) => false;
    public void Log(NHibernateLogLevel logLevel, NHibernateLogValues state, Exception? exception) { }
}

/// <summary>
/// Captures SQL text emitted to the <c>NHibernate.SQL</c> logger as low-fidelity statements
/// (SQL + session id only — no timing, parameters, rows, or object-load data). Stands down when
/// Tier A capture is active to avoid double-capture.
/// </summary>
internal sealed class SqlCapturingLogger : INHibernateLogger
{
    public bool IsEnabled(NHibernateLogLevel logLevel)
        => logLevel == NHibernateLogLevel.Debug && !NHibernautRuntime.TierAActive;

    public void Log(NHibernateLogLevel logLevel, NHibernateLogValues state, Exception? exception)
    {
        if (logLevel != NHibernateLogLevel.Debug || NHibernautRuntime.TierAActive) return;

        NHibernautRuntime.SafeExecute(() =>
        {
            var sql = state.ToString();
            if (string.IsNullOrWhiteSpace(sql)) return;

            var sessionId = SessionIdLoggingContext.SessionId ?? Guid.Empty;
            if (!NHibernautRuntime.IsSampled(sessionId)) return;
            var statement = new ProfiledStatement
            {
                Sql = sql,
                NormalizedSql = SqlNormalizer.Normalize(sql),
                SessionId = sessionId,
                StartedAt = DateTimeOffset.UtcNow,
                Kind = SqlKind.Of(sql)
            };

            var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
            lock (session.SyncRoot) session.Statements.Add(statement);
        });
    }
}
