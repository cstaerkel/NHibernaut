using System;
using System.Collections.Generic;
using System.Linq;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Core.Storage;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Fail-safe mandate (Spec 9): a throwing redactor or store must never surface to / break the host.
public class M5b_FailSafeIntegrationTests
{
    [Fact]
    public void Throwing_parameter_redactor_does_not_break_the_host_query()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(
            configureOptions: o => o.ParameterRedactor = _ => throw new InvalidOperationException("boom"));

        using (var seed = sf.OpenSession())
        using (var tx = seed.BeginTransaction())
        {
            seed.Save(new Widget { Name = "alpha" });
            tx.Commit();
        }

        // The parameterized query must still return correct results despite the redactor throwing.
        using var session = sf.OpenSession();
        var hits = session.Query<Widget>().Where(w => w.Name == "alpha").ToList();
        Assert.Single(hits);
    }

    [Fact]
    public void Throwing_store_does_not_break_the_host_query()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        using (var seed = sf.OpenSession())
        using (var tx = seed.BeginTransaction())
        {
            seed.Save(new Widget { Name = "x" });
            tx.Commit();
        }

        var original = NHibernautRuntime.Store;
        try
        {
            NHibernautRuntime.Store = new ThrowingStore();
            using var session = sf.OpenSession();
            var all = session.Query<Widget>().ToList(); // capture hits the throwing store, must be swallowed
            Assert.Single(all);
        }
        finally
        {
            NHibernautRuntime.Store = original;
        }
    }

    private sealed class ThrowingStore : IProfilerStore
    {
        public ProfiledSession GetOrCreateSession(Guid sessionId) => throw new InvalidOperationException("boom");
        public ProfiledSession? GetSession(Guid sessionId) => throw new InvalidOperationException("boom");
        public IReadOnlyList<ProfiledSession> GetRecentSessions(int take) => throw new InvalidOperationException("boom");
        public void SealSession(Guid sessionId) => throw new InvalidOperationException("boom");
        public void Clear() => throw new InvalidOperationException("boom");
        public int Count => throw new InvalidOperationException("boom");
        public event Action<ProfiledSession>? SessionSealed { add { } remove { } }
    }
}
