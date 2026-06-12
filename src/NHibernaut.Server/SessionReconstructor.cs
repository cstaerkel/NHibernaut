using System;
using System.Data;
using NHibernaut.Core.Model;

namespace NHibernaut.Server;

/// <summary>
/// Rebuilds a <see cref="ProfiledSession"/> from a <see cref="SessionDetailDto"/> received over the
/// wire — the inverse of <see cref="DtoMapper.ToDetail"/>. Used by the ingestion endpoint so a
/// forwarded session lands in the store and flows through the normal dashboard query / aggregate /
/// SSE path unchanged. Guids and enums are re-parsed from their string forms; parameter values are
/// carried as display strings (the dashboard only ever displays them).
/// </summary>
internal static class SessionReconstructor
{
    public static ProfiledSession FromDetail(SessionDetailDto dto)
    {
        var summary = dto.Summary;
        var session = new ProfiledSession
        {
            Id = Guid.Parse(summary.Id),
            StartedAt = summary.StartedAt,
            EndedAt = summary.EndedAt,
            IsSealed = true
        };

        // Individual thread ids aren't carried; synthesize so ThreadCount round-trips for display.
        for (var i = 0; i < summary.ThreadCount; i++) session.ThreadIds.Add(i);

        foreach (var st in dto.Statements) session.Statements.Add(ToStatement(st));
        foreach (var c in dto.Connections) session.Connections.Add(ToConnection(c));
        foreach (var t in dto.Transactions) session.Transactions.Add(ToTransaction(t));
        foreach (var e in dto.EntityLoads)
            session.EntityLoads.Add(new EntityLoad { EntityType = e.EntityType, Id = e.Id, StatementId = ParseGuidOrNull(e.StatementId) });
        foreach (var w in dto.Writes)
            session.Writes.Add(new EntityWrite { Kind = ParseEnum<WriteKind>(w.Kind), EntityType = w.EntityType, Id = w.Id, StatementId = ParseGuidOrNull(w.StatementId), NoActualChange = w.NoActualChange });
        foreach (var ci in dto.CollectionInits)
            session.CollectionInits.Add(new CollectionInit { Role = ci.Role, StatementId = ParseGuidOrNull(ci.StatementId) });
        foreach (var a in dto.Alerts) session.Alerts.Add(ToAlert(a));

        return session;
    }

    private static ProfiledStatement ToStatement(StatementDto st)
    {
        var statement = new ProfiledStatement
        {
            Id = Guid.Parse(st.Id),
            SessionId = Guid.Parse(st.SessionId),
            Sql = st.Sql,
            NormalizedSql = st.NormalizedSql,
            Kind = ParseEnum<StatementKind>(st.Kind),
            StartedAt = st.StartedAt,
            DurationMs = st.DurationMs,
            RowsAffected = st.RowsAffected,
            RowsRead = st.RowsRead,
            Exception = st.Exception,
            StackTrace = st.StackTrace,
            EntityLoadCount = st.EntityLoadCount,
            CollectionInitCount = st.CollectionInitCount
        };
        foreach (var p in st.Parameters)
            statement.Parameters.Add(new ParamCapture
            {
                Name = p.Name,
                DbType = p.DbType,
                Value = p.Value, // display string; the model boxes object? and the dashboard re-stringifies it
                Size = p.Size,
                Direction = ParseEnum<ParameterDirection>(p.Direction)
            });
        return statement;
    }

    private static ProfiledConnection ToConnection(ConnectionDto c)
    {
        var connection = new ProfiledConnection
        {
            Id = Guid.Parse(c.Id),
            OpenedAt = c.OpenedAt,
            ClosedAt = c.ClosedAt
        };
        foreach (var sid in c.StatementIds) connection.StatementIds.Add(Guid.Parse(sid));
        return connection;
    }

    private static ProfiledTransaction ToTransaction(TransactionDto t) => new()
    {
        Id = Guid.Parse(t.Id),
        BeganAt = t.BeganAt,
        CompletedAt = t.CompletedAt,
        Outcome = ParseEnum<TransactionOutcome>(t.Outcome)
    };

    private static Alert ToAlert(AlertDto a)
    {
        var alert = new Alert
        {
            Id = Guid.Parse(a.Id),
            Type = a.Type,
            Severity = ParseEnum<AlertSeverity>(a.Severity),
            Title = a.Title,
            Description = a.Description,
            Suggestion = a.Suggestion
        };
        foreach (var sid in a.RelatedStatementIds) alert.RelatedStatementIds.Add(Guid.Parse(sid));
        return alert;
    }

    private static TEnum ParseEnum<TEnum>(string value) where TEnum : struct
        => Enum.Parse<TEnum>(value, ignoreCase: true);

    private static Guid? ParseGuidOrNull(string? value)
        => string.IsNullOrEmpty(value) ? null : Guid.Parse(value);
}
