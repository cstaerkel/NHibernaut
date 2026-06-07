using System;
using System.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Core.Storage;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 1: scaffolding — options defaults, model aggregates, in-memory store retention.
public class M1_OptionsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var o = new NHibernautOptions();

        // alert thresholds (Spec section 7)
        Assert.Equal(10, o.NPlusOneThreshold);
        Assert.Equal(50, o.TooManyQueriesThreshold);
        Assert.Equal(100, o.UnboundedResultSetRowThreshold);
        Assert.Equal(1000, o.TooManyRowsThreshold);
        Assert.Equal(5, o.TooManyJoinsThreshold);
        Assert.Equal(200, o.SlowQueryMs);
        Assert.Equal(50, o.TooManyWritesThreshold);

        // capture options
        Assert.True(o.CaptureParameterValues);
        Assert.Equal(1.0, o.SamplingRate);
        Assert.Equal(200, o.RetentionSessionCount);
        Assert.Equal(TimeSpan.FromMinutes(30), o.RetentionMaxAge);

        // dashboard
        Assert.False(o.Dashboard.AutoStartServer);
        Assert.Equal("127.0.0.1", o.Dashboard.BindAddress);
        Assert.Equal(5005, o.Dashboard.Port);
        Assert.Null(o.Dashboard.AuthToken);
        Assert.False(o.Dashboard.EnabledInProduction);
        Assert.Equal("vscode", o.Dashboard.EditorLinkScheme);
    }
}

public class M1_SessionAggregateTests
{
    [Fact]
    public void Derived_aggregates_compute()
    {
        var s = new ProfiledSession { Id = Guid.NewGuid(), StartedAt = DateTimeOffset.UtcNow };
        s.Statements.Add(new ProfiledStatement { Sql = "a", DurationMs = 10, RowsRead = 5, Kind = StatementKind.Select });
        s.Statements.Add(new ProfiledStatement { Sql = "b", DurationMs = 20, RowsRead = 3, Kind = StatementKind.Select });
        s.Writes.Add(new EntityWrite { Kind = WriteKind.Insert, EntityType = "Foo" });
        s.EntityLoads.Add(new EntityLoad { EntityType = "Foo" });
        s.EntityLoads.Add(new EntityLoad { EntityType = "Foo" });
        s.EntityLoads.Add(new EntityLoad { EntityType = "Bar" });
        s.Alerts.Add(new Alert { Type = "X", Severity = AlertSeverity.Warning });
        s.Alerts.Add(new Alert { Type = "Y", Severity = AlertSeverity.Error });

        Assert.Equal(2, s.StatementCount);
        Assert.Equal(30, s.TotalDurationMs);
        Assert.Equal(8, s.TotalRowsRead);
        Assert.Equal(1, s.WriteCount);
        Assert.Equal(AlertSeverity.Error, s.MaxSeverity);
        Assert.Equal(2, s.EntityCountsByType["Foo"]);
        Assert.Equal(1, s.EntityCountsByType["Bar"]);
    }

    [Fact]
    public void MaxSeverity_null_when_no_alerts()
    {
        var s = new ProfiledSession { Id = Guid.NewGuid() };
        Assert.Null(s.MaxSeverity);
    }
}

public class M1_StoreTests
{
    private static InMemoryProfilerStore NewStore(int count = 200, TimeSpan? maxAge = null, Func<DateTimeOffset>? clock = null)
    {
        var opt = new NHibernautOptions { RetentionSessionCount = count };
        if (maxAge is not null) opt.RetentionMaxAge = maxAge.Value;
        return new InMemoryProfilerStore(opt, clock);
    }

    [Fact]
    public void GetOrCreate_returns_same_instance_for_same_id()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        var a = store.GetOrCreateSession(id);
        var b = store.GetOrCreateSession(id);
        Assert.Same(a, b);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Recent_returns_most_recent_first()
    {
        var store = NewStore();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        store.GetOrCreateSession(id1);
        store.GetOrCreateSession(id2);

        var recent = store.GetRecentSessions(10);
        Assert.Equal(id2, recent[0].Id);
        Assert.Equal(id1, recent[1].Id);
    }

    [Fact]
    public void Count_eviction_prunes_oldest()
    {
        var store = NewStore(count: 3);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        foreach (var id in ids) store.GetOrCreateSession(id);

        Assert.Equal(3, store.Count);
        Assert.Null(store.GetSession(ids[0]));
        Assert.Null(store.GetSession(ids[1]));
        Assert.NotNull(store.GetSession(ids[4]));
    }

    [Fact]
    public void Age_eviction_prunes_old_sessions()
    {
        var now = DateTimeOffset.UtcNow;
        var current = now;
        var store = NewStore(maxAge: TimeSpan.FromMinutes(30), clock: () => current);

        var oldId = Guid.NewGuid();
        store.GetOrCreateSession(oldId);

        current = now.AddMinutes(31);
        var newId = Guid.NewGuid();
        store.GetOrCreateSession(newId); // triggers prune

        Assert.Null(store.GetSession(oldId));
        Assert.NotNull(store.GetSession(newId));
    }

    [Fact]
    public void Seal_sets_ended_and_sealed()
    {
        var store = NewStore();
        var id = Guid.NewGuid();
        store.GetOrCreateSession(id);
        store.SealSession(id);

        var session = store.GetSession(id)!;
        Assert.True(session.IsSealed);
        Assert.NotNull(session.EndedAt);
    }

    [Fact]
    public void Clear_empties_store()
    {
        var store = NewStore();
        store.GetOrCreateSession(Guid.NewGuid());
        store.Clear();
        Assert.Equal(0, store.Count);
    }
}
