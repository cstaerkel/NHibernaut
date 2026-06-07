using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.AspNetCore;
using NHibernaut.Core;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

// Milestone 9 (optional Tier C): ASP.NET Core integration — request scoping, headers, mounted dashboard.
public class M9_AspNetCoreTests
{
    private static IHost BuildHost(ISessionFactory sf, string environment = "Development")
    {
        return new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .UseEnvironment(environment)
                .ConfigureServices(services =>
                {
                    services.AddNHibernaut();
                    services.AddSingleton(sf);
                })
                .Configure(app =>
                {
                    app.UseNHibernaut();
                    app.MapNHibernaut("/nhibernaut");
                    app.Run(async ctx =>
                    {
                        var factory = ctx.RequestServices.GetRequiredService<ISessionFactory>();
                        using var session = factory.OpenSession();
                        _ = session.Query<Widget>().ToList(); // DB work under the request scope
                        await ctx.Response.WriteAsync("ok");
                    });
                }))
            .Start();
    }

    [Fact]
    public async Task Request_gets_ServerTiming_and_RequestId_headers()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        using (var seed = sf.OpenSession())
        using (var tx = seed.BeginTransaction()) { seed.Save(new Widget { Name = "x" }); tx.Commit(); }

        using var host = BuildHost(sf);
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/work");
        resp.EnsureSuccessStatusCode();

        Assert.True(resp.Headers.Contains("X-NHibernaut-RequestId"));
        Assert.True(resp.Headers.TryGetValues("Server-Timing", out var timing));
        Assert.Contains("db;dur=", string.Join("", timing!));
    }

    [Fact]
    public async Task Mounted_dashboard_serves_api_and_assets()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        using (var seed = sf.OpenSession())
        using (var tx = seed.BeginTransaction()) { seed.Save(new Widget { Name = "x" }); tx.Commit(); }

        using var host = BuildHost(sf);
        var client = host.GetTestClient();

        // generate a session via the app endpoint, then read it back through the mounted API
        await client.GetAsync("/work");

        var json = await client.GetStringAsync("/nhibernaut/api/sessions");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var html = await client.GetAsync("/nhibernaut/");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Contains("NHibernaut", await html.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_is_hidden_in_production_by_default()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        using var host = BuildHost(sf, environment: "Production");
        var client = host.GetTestClient();

        var resp = await client.GetAsync("/nhibernaut/api/sessions");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
