using System;
using System.Collections.Generic;
using System.Linq;
using NHibernate;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 3: event listeners + correlation — entity loads, collection inits, writes,
// attributed to the executing statement and grouped by session.
public class M3_EventListenerTests
{
    private static readonly Action<ModelMapper> CustomerOrderMappings = m =>
    {
        m.AddMapping<CustomerMap>();
        m.AddMapping<OrderMap>();
    };

    [Fact]
    public void Get_of_N_entities_attributes_and_counts_loads_by_type()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        var ids = new List<int>();
        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            for (var i = 0; i < 3; i++)
            {
                var w = new Widget { Name = $"w{i}" };
                session.Save(w);
                ids.Add(w.Id);
            }
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            foreach (var id in ids)
                Assert.NotNull(session.Get<Widget>(id));
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Equal(3, profiled!.EntityCountsByType[typeof(Widget).FullName!]);
        Assert.All(profiled.EntityLoads, el => Assert.NotNull(el.StatementId));
    }

    [Fact]
    public void Lazy_collection_loop_captures_collection_inits_and_child_loads()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(CustomerOrderMappings);

        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            for (var i = 0; i < 3; i++)
            {
                var customer = new Customer { Name = $"c{i}" };
                for (var j = 0; j < 2; j++)
                    customer.Orders.Add(new Order { Description = $"o{i}-{j}", Customer = customer });
                session.Save(customer);
            }
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            var customers = session.Query<Customer>().ToList(); // 1 SELECT
            foreach (var customer in customers)
                foreach (var order in customer.Orders) // genuine lazy load: 1 SELECT per customer
                    _ = order.Description;
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);

        // 3 lazy collection initializations, one per customer's Orders
        Assert.Equal(3, profiled!.CollectionInits.Count);
        Assert.All(profiled.CollectionInits, ci => Assert.Contains("Orders", ci.Role));

        // entity loads counted by type
        Assert.Equal(3, profiled.EntityCountsByType[typeof(Customer).FullName!]);
        Assert.Equal(6, profiled.EntityCountsByType[typeof(Order).FullName!]);

        // 1 customers query + 3 collection-init queries
        Assert.Equal(4, profiled.Statements.Count);
    }

    [Fact]
    public void Insert_update_delete_capture_writes_by_kind()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();

            var w = new Widget { Name = "x" };
            session.Save(w);
            session.Flush();

            w.Name = "y";
            session.Update(w);
            session.Flush();

            session.Delete(w);
            tx.Commit();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Writes, w => w.Kind == WriteKind.Insert);
        Assert.Contains(profiled.Writes, w => w.Kind == WriteKind.Update);
        Assert.Contains(profiled.Writes, w => w.Kind == WriteKind.Delete);
    }

    [Fact]
    public void Created_object_carries_type_and_primary_key_attributed_to_its_statement()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        int widgetId;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            using var tx = session.BeginTransaction();
            var w = new Widget { Name = "created" };
            session.Save(w);
            widgetId = w.Id;
            tx.Commit();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid)!;
        var insert = Assert.Single(profiled.Writes, w => w.Kind == WriteKind.Insert);

        // type + primary key are captured
        Assert.Equal(typeof(Widget).FullName, insert.EntityType);
        Assert.Equal(widgetId, Convert.ToInt32(insert.Id));

        // and the write is attributed to the INSERT statement so the dashboard can show it per query
        Assert.NotNull(insert.StatementId);
        var statement = Assert.Single(profiled.Statements, s => s.Id == insert.StatementId);
        Assert.Equal(StatementKind.Insert, statement.Kind);
    }
}
