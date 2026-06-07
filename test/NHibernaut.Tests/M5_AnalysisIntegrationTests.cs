using System;
using System.Linq;
using System.Threading;
using NHibernate;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernaut.Core;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 5: detectors firing against REAL captured activity (no synthetic streams).
public class M5_AnalysisIntegrationTests
{
    private static readonly Action<ModelMapper> CustomerOrderMappings = m =>
    {
        m.AddMapping<CustomerMap>();
        m.AddMapping<OrderMap>();
    };

    [Fact]
    public void Genuine_lazy_loop_raises_SelectNPlusOne()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(CustomerOrderMappings);

        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            for (var i = 0; i < 12; i++)
            {
                var customer = new Customer { Name = $"c{i}" };
                customer.Orders.Add(new Order { Description = "o", Customer = customer });
                session.Save(customer);
            }
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            var customers = session.Query<Customer>().ToList();
            foreach (var customer in customers)
                foreach (var order in customer.Orders) // genuine N+1: a SELECT per customer
                    _ = order.Description;
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Alerts, a => a.Type == "SelectNPlusOne");
    }

    [Fact]
    public void Unbounded_large_select_raises_UnboundedResultSet()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            for (var i = 0; i < 150; i++) session.Save(new Widget { Name = $"w{i}" });
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList(); // no limit, 150 rows
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Alerts, a => a.Type == "UnboundedResultSet");
    }

    [Fact]
    public void Insert_outside_transaction_raises_WriteWithoutTransaction()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        NHibernautRuntime.Store.Clear();

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            session.Save(new Widget { Name = "x" }); // identity generator -> immediate INSERT, no tx
            session.Flush();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Alerts, a => a.Type == "WriteWithoutTransaction");
    }

    [Fact]
    public void Session_used_on_two_threads_raises_CrossThreadSession()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        using (var seed = sf.OpenSession())
        using (var tx = seed.BeginTransaction())
        {
            seed.Save(new Widget { Name = "x" });
            tx.Commit();
        }

        NHibernautRuntime.Store.Clear();

        var session = sf.OpenSession();
        var sid = session.GetSessionImplementation().SessionId;

        session.Query<Widget>().ToList(); // current thread

        // A dedicated thread guarantees a distinct managed thread id (sequential, joined).
        var worker = new Thread(() => session.Query<Widget>().ToList());
        worker.Start();
        worker.Join();

        session.Dispose(); // seal + analyze

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        Assert.Contains(profiled!.Alerts, a => a.Type == "CrossThreadSession");
    }
}
