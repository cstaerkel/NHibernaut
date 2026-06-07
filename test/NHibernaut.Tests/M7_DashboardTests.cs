using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using NHibernate;
using NHibernate.Linq;
using NHibernaut.Core;
using NHibernaut.Server;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

public class M7_DashboardTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static NHibernautOptions LoopbackOptions()
        => new() { Dashboard = { BindAddress = "127.0.0.1", Port = FreePort() } };

    [Fact]
    public async Task Root_serves_embedded_dashboard_html()
    {
        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var resp = await http.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("text/html; charset=utf-8", resp.Content.Headers.ContentType!.ToString());

        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("NHibernaut", html);
        Assert.Contains("app.js", html);
    }

    [Fact]
    public async Task Static_assets_are_served_with_correct_content_type()
    {
        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var js = await http.GetAsync("/app.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
        Assert.Contains("javascript", js.Content.Headers.ContentType!.ToString());

        var css = await http.GetAsync("/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("css", css.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public void Stack_trace_is_captured_when_enabled_for_click_to_source()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory(configureOptions: o =>
        {
            o.CaptureStackTraces = true;
            // The default filter strips "NHibernaut." which here also strips the test's own frames
            // (NHibernaut.Tests). Use a filter that strips framework + the profiler's real namespaces,
            // leaving the "app" (test) frames — as it would in a real consumer app (MyApp.*).
            o.StackTraceNamespaceFilter = new[] { "NHibernate.", "System.", "NHibernaut.Core", "NHibernaut.Server" };
        });

        Guid sid;
        using (var session = sf.OpenSession())
        {
            sid = session.GetSessionImplementation().SessionId;
            _ = session.Query<Widget>().ToList();
        }

        var profiled = NHibernautRuntime.Store.GetSession(sid);
        Assert.NotNull(profiled);
        var stmt = profiled!.Statements.First();
        Assert.False(string.IsNullOrEmpty(stmt.StackTrace));
        // the app frame (this test) remains and carries file:line for click-to-source
        Assert.Contains("M7_DashboardTests", stmt.StackTrace!);
        Assert.Contains(".cs:", stmt.StackTrace!);
    }
}
