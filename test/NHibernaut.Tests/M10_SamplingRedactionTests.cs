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

// Milestone 10: sampling + parameter redaction options.
public class M10_SamplingRedactionTests : IDisposable
{
    private static void Seed(ISessionFactory sf)
    {
        using var session = sf.OpenSession();
        using var tx = session.BeginTransaction();
        session.Save(new Widget { Name = "alpha" });
        tx.Commit();
    }

    [Fact]
    public void SamplingRate_zero_captures_nothing()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(configureOptions: o => o.SamplingRate = 0.0);
        Seed(sf);
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList();
        }

        Assert.Null(NHibernautRuntime.Store.GetSession(sid));
    }

    [Fact]
    public void SamplingRate_one_captures_session()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(configureOptions: o => o.SamplingRate = 1.0);
        Seed(sf);
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList();
        }

        Assert.NotNull(NHibernautRuntime.Store.GetSession(sid));
    }

    [Fact]
    public void CaptureParameterValues_false_omits_values()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(configureOptions: o => o.CaptureParameterValues = false);
        Seed(sf);
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().Where(w => w.Name == "alpha").ToList();
        }

        var stmt = NHibernautRuntime.Store.GetSession(sid)!.Statements.First(s => s.Parameters.Count > 0);
        Assert.All(stmt.Parameters, p => Assert.Null(p.Value));
    }

    [Fact]
    public void ParameterRedactor_masks_values()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(configureOptions: o => o.ParameterRedactor = _ => "***");
        Seed(sf);
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().Where(w => w.Name == "alpha").ToList();
        }

        var stmt = NHibernautRuntime.Store.GetSession(sid)!.Statements.First(s => s.Parameters.Count > 0);
        Assert.Contains(stmt.Parameters, p => Equals(p.Value, "***"));
        Assert.DoesNotContain(stmt.Parameters, p => Equals(p.Value, "alpha"));
    }

    // EnableNHibernaut (via BuildProfiledSessionFactory) mutates global NHibernautRuntime state. Reset
    // it after each test so per-test option overrides — notably SamplingRate=0.0 — don't leak into a
    // later test (this was the cause of an order-dependent failure in M8's Tier B baseline test).
    public void Dispose()
    {
        NHibernautRuntime.Options = new NHibernautOptions();
        NHibernautRuntime.Store = new InMemoryProfilerStore();
        NHibernautRuntime.TierAActive = false;
    }
}
