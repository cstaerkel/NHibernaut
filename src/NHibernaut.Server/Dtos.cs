using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Model;

namespace NHibernaut.Server;

// Wire DTOs decouple the JSON API from the internal model (no SyncRoot/locks on the wire) and
// render values as display-ready strings. All mapping snapshots under the session lock.

public sealed record SessionSummaryDto(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool IsSealed,
    int StatementCount,
    double TotalDurationMs,
    int TotalRowsRead,
    int WriteCount,
    int AlertCount,
    string? MaxSeverity,
    int ThreadCount);

public sealed record ParamDto(string? Name, string? DbType, string? Value, int Size, string Direction);

public sealed record StatementDto(
    string Id,
    string SessionId,
    string Sql,
    string? NormalizedSql,
    string Kind,
    DateTimeOffset StartedAt,
    double DurationMs,
    int? RowsAffected,
    int? RowsRead,
    string? Exception,
    string? StackTrace,
    int EntityLoadCount,
    int CollectionInitCount,
    IReadOnlyList<ParamDto> Parameters);

public sealed record ConnectionDto(string Id, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt, IReadOnlyList<string> StatementIds);

public sealed record TransactionDto(string Id, DateTimeOffset BeganAt, DateTimeOffset? CompletedAt, string Outcome);

public sealed record EntityLoadDto(string EntityType, string? Id, string? StatementId);

public sealed record EntityWriteDto(string Kind, string EntityType, string? Id, string? StatementId, bool NoActualChange);

public sealed record CollectionInitDto(string Role, string? StatementId);

public sealed record AlertDto(string Id, string Type, string Severity, string Title, string Description, string? Suggestion, IReadOnlyList<string> RelatedStatementIds);

public sealed record SessionDetailDto(
    SessionSummaryDto Summary,
    IReadOnlyList<StatementDto> Statements,
    IReadOnlyList<ConnectionDto> Connections,
    IReadOnlyList<TransactionDto> Transactions,
    IReadOnlyList<EntityLoadDto> EntityLoads,
    IReadOnlyList<EntityWriteDto> Writes,
    IReadOnlyList<CollectionInitDto> CollectionInits,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyDictionary<string, int> EntityCountsByType);

public sealed record AggregateRowDto(
    string NormalizedSql,
    int ExecutionCount,
    double TotalDurationMs,
    double AvgDurationMs,
    int MaxRowsRead,
    int SessionCount,
    int NPlusOneIncidence);

internal static class DtoMapper
{
    private static string? Str(object? value) =>
        value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    public static SessionSummaryDto ToSummary(ProfiledSession s)
    {
        lock (s.SyncRoot)
        {
            return ToSummaryUnlocked(s);
        }
    }

    private static SessionSummaryDto ToSummaryUnlocked(ProfiledSession s) => new(
        s.Id.ToString(),
        s.StartedAt,
        s.EndedAt,
        s.IsSealed,
        s.StatementCount,
        s.TotalDurationMs,
        s.TotalRowsRead,
        s.WriteCount,
        s.Alerts.Count,
        s.MaxSeverity?.ToString(),
        s.ThreadIds.Count);

    public static SessionDetailDto ToDetail(ProfiledSession s)
    {
        lock (s.SyncRoot)
        {
            return new SessionDetailDto(
                ToSummaryUnlocked(s),
                s.Statements.Select(ToStatement).ToList(),
                s.Connections.Select(c => new ConnectionDto(
                    c.Id.ToString(), c.OpenedAt, c.ClosedAt, c.StatementIds.Select(x => x.ToString()).ToList())).ToList(),
                s.Transactions.Select(t => new TransactionDto(
                    t.Id.ToString(), t.BeganAt, t.CompletedAt, t.Outcome.ToString())).ToList(),
                s.EntityLoads.Select(e => new EntityLoadDto(e.EntityType, Str(e.Id), e.StatementId?.ToString())).ToList(),
                s.Writes.Select(w => new EntityWriteDto(w.Kind.ToString(), w.EntityType, Str(w.Id), w.StatementId?.ToString(), w.NoActualChange)).ToList(),
                s.CollectionInits.Select(c => new CollectionInitDto(c.Role, c.StatementId?.ToString())).ToList(),
                s.Alerts.Select(ToAlert).ToList(),
                new Dictionary<string, int>(s.EntityCountsByType));
        }
    }

    public static StatementDto ToStatement(ProfiledStatement st) => new(
        st.Id.ToString(),
        st.SessionId.ToString(),
        st.Sql,
        st.NormalizedSql,
        st.Kind.ToString(),
        st.StartedAt,
        st.DurationMs,
        st.RowsAffected,
        st.RowsRead,
        st.Exception,
        st.StackTrace,
        st.EntityLoadCount,
        st.CollectionInitCount,
        st.Parameters.Select(p => new ParamDto(p.Name, p.DbType, Str(p.Value), p.Size, p.Direction.ToString())).ToList());

    public static AlertDto ToAlert(Alert a) => new(
        a.Id.ToString(),
        a.Type,
        a.Severity.ToString(),
        a.Title,
        a.Description,
        a.Suggestion,
        a.RelatedStatementIds.Select(x => x.ToString()).ToList());

    public static IReadOnlyList<AggregateRowDto> ToAggregate(IEnumerable<ProfiledSession> sessions, NHibernautOptions options)
    {
        // Snapshot statements per session under lock, then aggregate by normalized shape.
        var perSession = new List<List<ProfiledStatement>>();
        foreach (var s in sessions)
        {
            lock (s.SyncRoot) perSession.Add(s.Statements.ToList());
        }

        var rows = new Dictionary<string, AggAccumulator>();
        foreach (var statements in perSession)
        {
            var shapeCountsThisSession = statements
                .Where(st => !string.IsNullOrEmpty(st.NormalizedSql))
                .GroupBy(st => st.NormalizedSql!)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var st in statements)
            {
                var shape = st.NormalizedSql;
                if (string.IsNullOrEmpty(shape)) continue;
                if (!rows.TryGetValue(shape!, out var acc))
                {
                    acc = new AggAccumulator(shape!);
                    rows[shape!] = acc;
                }
                acc.Count++;
                acc.TotalDurationMs += st.DurationMs;
                acc.MaxRowsRead = Math.Max(acc.MaxRowsRead, st.RowsRead ?? 0);
            }

            foreach (var kv in shapeCountsThisSession)
            {
                var acc = rows[kv.Key];
                acc.SessionCount++;
                if (kv.Value >= options.NPlusOneThreshold) acc.NPlusOneIncidence++;
            }
        }

        return rows.Values
            .Select(a => new AggregateRowDto(
                a.NormalizedSql, a.Count, a.TotalDurationMs,
                a.Count == 0 ? 0 : a.TotalDurationMs / a.Count,
                a.MaxRowsRead, a.SessionCount, a.NPlusOneIncidence))
            .OrderByDescending(r => r.TotalDurationMs)
            .ToList();
    }

    private sealed class AggAccumulator
    {
        public AggAccumulator(string normalizedSql) => NormalizedSql = normalizedSql;
        public string NormalizedSql { get; }
        public int Count;
        public double TotalDurationMs;
        public int MaxRowsRead;
        public int SessionCount;
        public int NPlusOneIncidence;
    }
}
