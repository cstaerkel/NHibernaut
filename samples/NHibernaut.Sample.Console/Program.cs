using System;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernate.Tool.hbm2ddl;
using NHibernaut.Core;
using NHibernaut.Sample.ConsoleApp;
using NHibernaut.Server;

// NHibernaut console sample: self-hosts the dashboard and generates the kinds of activity the
// profiler is designed to surface — N+1, writes, transactions, a slow query, an unbounded select.

const string connectionString = "Data Source=nhibernaut_sample;Mode=Memory;Cache=Shared";

// Keep one connection open so the shared in-memory database survives between sessions.
using var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

var cfg = new Configuration();
cfg.DataBaseIntegration(db =>
{
    db.Driver<MicrosoftDataSqliteDriver>();
    db.Dialect<SQLiteDialect>();
    db.ConnectionString = connectionString;
    db.KeywordsAutoImport = Hbm2DDLKeyWords.None;
    db.LogSqlInConsole = false;
});

var mapper = new ModelMapper();
mapper.AddMapping<BlogMap>();
mapper.AddMapping<PostMap>();
cfg.AddMapping(mapper.CompileMappingForAllExplicitlyAddedEntities());

new SchemaExport(cfg).Execute(false, true, false, keepAlive, null);

// Two lines to enable profiling + the dashboard.
cfg.EnableNHibernaut(o => o.CaptureStackTraces = true);
var sessionFactory = cfg.BuildSessionFactory();
using var dashboard = NHibernautServer.Start(new NHibernautOptions { Dashboard = { Port = 5005 } });

Console.WriteLine("NHibernaut dashboard: http://localhost:5005");

// Seed enough data that the unbounded "SELECT * FROM posts" trips the >100-row detector.
using (var session = sessionFactory.OpenSession())
using (var tx = session.BeginTransaction())
{
    for (var b = 0; b < 30; b++)
    {
        var blog = new Blog { Name = $"Blog {b}" };
        for (var p = 0; p < 5; p++)
            blog.Posts.Add(new Post { Title = $"Post {b}-{p}", Blog = blog });
        session.Save(blog);
    }
    tx.Commit();
}

Workload.RunForever(sessionFactory, connectionString);

internal static class Workload
{
    public static void RunForever(NHibernate.ISessionFactory sf, string connectionString)
    {
        var iteration = 0;
        while (true)
        {
            GenerateOne(sf, connectionString, iteration++);
            Console.WriteLine($"workload iteration {iteration} — see http://localhost:5005");
            Thread.Sleep(3000);
        }
    }

    private static void GenerateOne(NHibernate.ISessionFactory sf, string connectionString, int iteration)
    {
        // 1) A classic N+1: load blogs, then touch each blog's lazy Posts collection in a loop.
        using (var session = sf.OpenSession())
        {
            var blogs = session.Query<Blog>().ToList();
            foreach (var blog in blogs)
                _ = blog.Posts.Count;
        }

        // 2) Writes inside a transaction.
        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            session.Save(new Blog { Name = $"Created at iteration {iteration}" });
            tx.Commit();
        }

        // 3) A write WITHOUT a transaction (identity generator inserts immediately).
        using (var session = sf.OpenSession())
        {
            session.Save(new Blog { Name = $"No-tx blog {iteration}" });
            session.Flush();
        }

        // 4) An unbounded large SELECT that the UnboundedResultSet detector flags (>100 rows).
        using (var session = sf.OpenSession())
        {
            _ = session.CreateSQLQuery("SELECT * FROM posts").List();
        }
    }
}
