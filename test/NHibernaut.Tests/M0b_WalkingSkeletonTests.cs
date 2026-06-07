using System;
using System.Linq;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 0b: the thinnest end-to-end slice. Proves that EnableNHibernaut() swaps the
// connection provider AND that capture actually fires for a real query. If this is green,
// the riskiest integration is de-risked and the rest is mostly additive.
public class M0b_WalkingSkeletonTests
{
    [Fact]
    public void EnableNHibernaut_captures_one_statement_for_one_query()
    {
        using var db = new SqliteTestDatabase();
        var cfg = db.BuildConfiguration();
        db.CreateSchema(cfg);

        cfg.EnableNHibernaut();

        using var sf = cfg.BuildSessionFactory();

        // Measure exactly the one query: clear the store right before it.
        NHibernautRuntime.Store.Clear();
        Guid sessionId;
        using (var session = sf.OpenSession())
        {
            sessionId = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList(); // exactly one SELECT
        }

        var profiled = NHibernautRuntime.Store.GetSession(sessionId);
        Assert.NotNull(profiled);
        Assert.Single(profiled!.Statements);
    }
}
