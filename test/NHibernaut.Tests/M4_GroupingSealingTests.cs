using System;
using System.Collections.Generic;
using System.Linq;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 4: connection grouping, aggregates over real activity, and session sealing on close.
public class M4_GroupingSealingTests
{
    [Fact]
    public void Session_records_connection_open_and_close_and_is_sealed()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();
            session.Save(new Widget { Name = "x" });
            tx.Commit();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.True(profiled!.IsSealed);
        Assert.NotNull(profiled.EndedAt);
        Assert.NotEmpty(profiled.Connections);
        Assert.All(profiled.Connections, c => Assert.NotNull(c.ClosedAt));
    }

    [Fact]
    public void SessionSealed_event_fires_with_the_session()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        var sealedIds = new List<Guid>();
        NHibernautRuntime.Store.SessionSealed += s => sealedIds.Add(s.Id);

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();
            session.Save(new Widget { Name = "x" });
            tx.Commit();
        }

        Assert.Contains(sid, sealedIds);
    }

    [Fact]
    public void Aggregates_reflect_real_captured_activity()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            for (var i = 0; i < 3; i++) session.Save(new Widget { Name = $"w{i}" });
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList();
            _ = session.Query<Widget>().ToList();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Equal(2, profiled!.StatementCount);
        Assert.True(profiled.TotalDurationMs >= 0);
        Assert.Equal(6, profiled.TotalRowsRead); // 3 rows x 2 queries
    }
}
