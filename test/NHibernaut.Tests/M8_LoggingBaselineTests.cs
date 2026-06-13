using System;
using System.Linq;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Storage;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 8: Tier B zero-touch logging baseline. The module initializer (run on Core load) installs
// the logger factory; the NHibernate.SQL logger captures SQL text when Tier A is not active.
public class M8_LoggingBaselineTests
{
    [Fact]
    public void Tier_B_captures_sql_without_EnableNHibernaut()
    {
        var prevTierA = NHibernautRuntime.TierAActive;
        var prevStore = NHibernautRuntime.Store;
        var prevOptions = NHibernautRuntime.Options;
        try
        {
            using var db = new SqliteTestDatabase();
            var cfg = db.BuildConfiguration();
            db.CreateSchema(cfg);
            using var sf = cfg.BuildSessionFactory(); // NO EnableNHibernaut — pure Tier B

            // Measure into a fresh store with the Tier B baseline active. Pin a default Options too: the
            // Tier B logger only records sampled sessions, and a prior test (e.g. M10's SamplingRate=0.0
            // case) can leave the global rate below 1.0, which would silently drop this session's SQL.
            // A default NHibernautOptions samples everything (rate 1.0).
            NHibernautRuntime.Options = new NHibernautOptions();
            NHibernautRuntime.TierAActive = false;
            NHibernautRuntime.Store = new InMemoryProfilerStore();

            using (var session = sf.OpenSession())
                _ = session.Query<Widget>().ToList();

            var captured = NHibernautRuntime.Store.GetRecentSessions(10)
                .SelectMany(s => s.Statements)
                .ToList();

            Assert.Contains(captured, st =>
                st.Sql.Contains("widgets", StringComparison.OrdinalIgnoreCase) &&
                st.Sql.Contains("select", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            NHibernautRuntime.Options = prevOptions;
            NHibernautRuntime.TierAActive = prevTierA;
            NHibernautRuntime.Store = prevStore;
        }
    }

    [Fact]
    public void Tier_B_stands_down_when_Tier_A_is_active()
    {
        var prevTierA = NHibernautRuntime.TierAActive;
        var prevStore = NHibernautRuntime.Store;
        try
        {
            using var db = new SqliteTestDatabase();
            var cfg = db.BuildConfiguration();
            db.CreateSchema(cfg);
            using var sf = cfg.BuildSessionFactory(); // plain SF (no command-wrapper capture)

            NHibernautRuntime.TierAActive = true; // Tier A active → baseline must NOT capture
            NHibernautRuntime.Store = new InMemoryProfilerStore();

            using (var session = sf.OpenSession())
                _ = session.Query<Widget>().ToList();

            var captured = NHibernautRuntime.Store.GetRecentSessions(10)
                .SelectMany(s => s.Statements)
                .ToList();

            Assert.Empty(captured);
        }
        finally
        {
            NHibernautRuntime.TierAActive = prevTierA;
            NHibernautRuntime.Store = prevStore;
        }
    }
}
