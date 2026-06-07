using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Dialect;
using NHibernate.Linq;
using NHibernate.Mapping.ByCode;
using NHibernate.Tool.hbm2ddl;
using NHibernaut.AspNetCore;
using NHibernaut.Core;
using NHibernaut.Sample.Web;

// NHibernaut ASP.NET Core (Tier C) sample: the dashboard is mounted on the host's own pipeline at
// /nhibernaut, and every request gets Server-Timing + X-NHibernaut-RequestId headers.

const string connectionString = "Data Source=nhibernaut_web;Mode=Memory;Cache=Shared";

// Keep one connection open so the shared in-memory database survives between sessions.
var keepAlive = new SqliteConnection(connectionString);
keepAlive.Open();

var cfg = new Configuration();
cfg.DataBaseIntegration(db =>
{
    db.Driver<MicrosoftDataSqliteDriver>();
    db.Dialect<SQLiteDialect>();
    db.ConnectionString = connectionString;
    db.KeywordsAutoImport = Hbm2DDLKeyWords.None;
});

var mapper = new ModelMapper();
mapper.AddMapping<BlogMap>();
mapper.AddMapping<PostMap>();
cfg.AddMapping(mapper.CompileMappingForAllExplicitlyAddedEntities());

new SchemaExport(cfg).Execute(false, true, false, keepAlive, null);

cfg.EnableNHibernaut(o => o.CaptureStackTraces = true); // Tier A capture still wires NHibernate
var sessionFactory = cfg.BuildSessionFactory();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(sessionFactory);
builder.Services.AddSingleton(keepAlive); // keep the in-memory DB alive for the app's lifetime
builder.Services.AddNHibernaut();

var app = builder.Build();
app.UseNHibernaut();   // request scoping + Server-Timing / X-NHibernaut-RequestId headers
app.MapNHibernaut();   // dashboard at /nhibernaut

// Seed some data.
using (var session = sessionFactory.OpenSession())
using (var tx = session.BeginTransaction())
{
    for (var b = 0; b < 6; b++)
    {
        var blog = new Blog { Name = $"Blog {b}" };
        for (var p = 0; p < 4; p++)
            blog.Posts.Add(new Post { Title = $"Post {b}-{p}", Blog = blog });
        session.Save(blog);
    }
    tx.Commit();
}

app.MapGet("/", () =>
    "NHibernaut web sample. Hit /blogs (triggers an N+1), then open the dashboard at /nhibernaut");

// A deliberately N+1 endpoint: load blogs, then touch each blog's lazy Posts collection.
app.MapGet("/blogs", (ISessionFactory sf) =>
{
    using var session = sf.OpenSession();
    var blogs = session.Query<Blog>().ToList();
    var result = blogs.Select(b => new { b.Id, b.Name, posts = b.Posts.Count }).ToList();
    return Results.Ok(result);
});

app.Run();
