using System.Linq;
using NHibernate.Linq;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 0a: prove the toolchain + a REAL NHibernate SessionFactory against SQLite works,
// before any feature code exists. If these are green, env failures are ruled out downstream.
public class M0a_EnvironmentSmokeTests
{
    [Fact]
    public void Toolchain_works()
    {
        Assert.True(true);
    }

    [Fact]
    public void Real_SessionFactory_builds_and_round_trips_against_sqlite()
    {
        using var db = new SqliteTestDatabase();
        var cfg = db.BuildConfiguration();
        db.CreateSchema(cfg);

        using var sf = cfg.BuildSessionFactory();

        // write
        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            session.Save(new Widget { Name = "alpha" });
            session.Save(new Widget { Name = "beta" });
            tx.Commit();
        }

        // read back
        using (var session = sf.OpenSession())
        {
            var names = session.Query<Widget>().Select(w => w.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "alpha", "beta" }, names);
        }
    }
}
