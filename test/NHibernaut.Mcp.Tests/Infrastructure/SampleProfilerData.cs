using System;
using System.Collections.Generic;
using NHibernaut.Server;

namespace NHibernaut.Mcp.Tests.Infrastructure;

public static class SampleProfilerData
{
    public static readonly Guid SessionAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid SessionBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid EmptySessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid StatementA1Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");
    public static readonly Guid StatementA2Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    public static readonly Guid StatementB1Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1");
    public static readonly Guid StatementB2Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");

    private static readonly DateTimeOffset BaseTime = new(2026, 6, 25, 12, 0, 0, TimeSpan.Zero);

    public static FakeDashboardClient Dashboard()
    {
        var client = new FakeDashboardClient();
        var sessionA = SessionA();
        var sessionB = SessionB();
        var empty = EmptySession();

        client.Sessions.Add(sessionA.Summary);
        client.Sessions.Add(sessionB.Summary);
        client.Sessions.Add(empty.Summary);
        client.Details[SessionAId] = sessionA;
        client.Details[SessionBId] = sessionB;
        client.Details[EmptySessionId] = empty;

        client.AlertFeed.Add(new AlertFeedItemDto(SessionAId.ToString(), BaseTime, new AlertDto(
            "alert-info", "Informational", "Info", "Info alert", "Only context", null, [])));
        client.AlertFeed.Add(new AlertFeedItemDto(SessionAId.ToString(), BaseTime, sessionA.Alerts[0]));
        client.AlertFeed.Add(new AlertFeedItemDto(SessionBId.ToString(), BaseTime.AddMinutes(5), sessionB.Alerts[0]));
        client.AlertFeed.Add(new AlertFeedItemDto(SessionAId.ToString(), BaseTime, new AlertDto(
            "alert-nplusone-2", "SelectNPlusOne", "Warning", "Second N+1", "Repeated shape", "Fetch joins can help.", [StatementA2Id.ToString()])));

        client.AggregateRows.Add(new AggregateRowDto("SELECT * FROM orders WHERE customer_id = ?", 6, 120, 20, 12, 2, 1));
        client.AggregateRows.Add(new AggregateRowDto("UPDATE widgets SET name = ?", 2, 60, 30, 0, 1, 0));
        client.AggregateRows.Add(new AggregateRowDto("SELECT * FROM widgets WHERE id = ?", 10, 50, 5, 3, 2, 2));

        return client;
    }

    private static SessionDetailDto SessionA()
    {
        var summary = new SessionSummaryDto(
            SessionAId.ToString(), BaseTime, BaseTime.AddSeconds(2), true, 3, 42, 13, 1, 1, "Warning", 1);
        var statements = new List<StatementDto>
        {
            new(
                StatementA1Id.ToString(),
                SessionAId.ToString(),
                "SELECT * FROM widgets WHERE id = @p0",
                "SELECT * FROM widgets WHERE id = ?",
                "Select",
                BaseTime,
                12.5,
                null,
                3,
                null,
                "at App.Repository.LoadWidget()",
                1,
                0,
                [new ParamDto("p0", "Int32", "42", 4, "Input")]),
            new(
                StatementA2Id.ToString(),
                SessionAId.ToString(),
                "SELECT * FROM orders WHERE customer_id = @p0",
                "SELECT * FROM orders WHERE customer_id = ?",
                "Select",
                BaseTime.AddMilliseconds(20),
                20,
                null,
                10,
                null,
                null,
                0,
                1,
                []),
            new(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3").ToString(),
                SessionAId.ToString(),
                "SELECT * FROM orders WHERE customer_id = @p0",
                "SELECT * FROM orders WHERE customer_id = ?",
                "Select",
                BaseTime.AddMilliseconds(40),
                9.5,
                null,
                0,
                null,
                null,
                0,
                0,
                []),
        };

        var alert = new AlertDto(
            "alert-nplusone",
            "SelectNPlusOne",
            "Warning",
            "Possible N+1",
            "The same order query shape repeated.",
            "Use fetch joins, batch fetching, or projection.",
            [StatementA2Id.ToString()]);

        return new SessionDetailDto(
            summary,
            statements,
            [],
            [],
            [new EntityLoadDto("Widget", "42", StatementA1Id.ToString())],
            [new EntityWriteDto("Insert", "Widget", "42", StatementA1Id.ToString(), false)],
            [new CollectionInitDto("Widget.Orders", StatementA2Id.ToString())],
            [alert],
            new Dictionary<string, int> { ["Widget"] = 1 });
    }

    private static SessionDetailDto SessionB()
    {
        var summary = new SessionSummaryDto(
            SessionBId.ToString(), BaseTime.AddMinutes(5), BaseTime.AddMinutes(5).AddSeconds(4), true, 2, 95, 40, 0, 1, "Error", 1);
        var statements = new List<StatementDto>
        {
            new(
                StatementB1Id.ToString(),
                SessionBId.ToString(),
                "SELECT * FROM widgets WHERE id = @p0",
                "SELECT * FROM widgets WHERE id = ?",
                "Select",
                BaseTime.AddMinutes(5),
                70,
                null,
                40,
                null,
                null,
                2,
                0,
                []),
            new(
                StatementB2Id.ToString(),
                SessionBId.ToString(),
                "DELETE FROM widgets WHERE id = @p0",
                "DELETE FROM widgets WHERE id = ?",
                "Delete",
                BaseTime.AddMinutes(5).AddMilliseconds(50),
                25,
                1,
                null,
                null,
                null,
                0,
                0,
                []),
        };

        var alert = new AlertDto(
            "alert-slow",
            "SlowQuery",
            "Error",
            "Slow query",
            "A statement exceeded the slow-query threshold.",
            "Inspect the execution plan and indexes.",
            [StatementB1Id.ToString()]);

        return new SessionDetailDto(summary, statements, [], [], [], [], [], [alert], new Dictionary<string, int>());
    }

    private static SessionDetailDto EmptySession()
    {
        var summary = new SessionSummaryDto(
            EmptySessionId.ToString(), BaseTime.AddMinutes(10), BaseTime.AddMinutes(10), true, 0, 0, 0, 0, 0, null, 1);

        return new SessionDetailDto(summary, [], [], [], [], [], [], [], new Dictionary<string, int>());
    }
}
