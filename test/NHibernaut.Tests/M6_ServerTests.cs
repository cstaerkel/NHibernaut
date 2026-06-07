using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NHibernate;
using NHibernaut.Core;
using NHibernaut.Server;
using NHibernaut.Tests.Domain;
using NHibernaut.Tests.Infrastructure;
using Xunit;

namespace NHibernaut.Tests;

public class M6_ServerTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static NHibernautOptions LoopbackOptions(string? token = null)
        => new() { Dashboard = { BindAddress = "127.0.0.1", Port = FreePort(), AuthToken = token } };

    private static void GenerateSession(ISessionFactory sf)
    {
        using var session = sf.OpenSession();
        using var tx = session.BeginTransaction();
        session.Save(new Widget { Name = "alpha" });
        tx.Commit();
    }

    [Fact]
    public async Task Sessions_endpoint_returns_captured_sessions()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        GenerateSession(sf);

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var json = await http.GetStringAsync("/api/sessions");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() >= 1);
        Assert.True(doc.RootElement[0].TryGetProperty("statementCount", out _));
    }

    [Fact]
    public async Task Session_detail_endpoint_returns_statements()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        Guid sid;
        using (var session = sf.OpenSession())
        using (var tx = session.BeginTransaction())
        {
            sid = session.GetSessionImplementation().SessionId;
            session.Save(new Widget { Name = "x" });
            tx.Commit();
        }

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var json = await http.GetStringAsync($"/api/sessions/{sid}");
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("statements", out var statements));
        Assert.True(statements.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Unknown_session_detail_returns_404()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var resp = await http.GetAsync($"/api/sessions/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Aggregate_and_alerts_endpoints_return_json_arrays()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        GenerateSession(sf);

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        using var agg = JsonDocument.Parse(await http.GetStringAsync("/api/aggregate"));
        Assert.Equal(JsonValueKind.Array, agg.RootElement.ValueKind);

        using var alerts = JsonDocument.Parse(await http.GetStringAsync("/api/alerts"));
        Assert.Equal(JsonValueKind.Array, alerts.RootElement.ValueKind);
    }

    [Fact]
    public async Task Delete_sessions_clears_the_store()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        GenerateSession(sf);

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var del = await http.DeleteAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        using var doc = JsonDocument.Parse(await http.GetStringAsync("/api/sessions"));
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public void Non_loopback_bind_without_token_refuses_to_start()
    {
        var options = new NHibernautOptions { Dashboard = { BindAddress = "0.0.0.0", Port = FreePort(), AuthToken = null } };
        Assert.Throws<InvalidOperationException>(() => NHibernautServer.Start(options));
    }

    [Fact]
    public async Task Auth_token_is_enforced_on_every_request()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();
        GenerateSession(sf);

        var options = LoopbackOptions(token: "s3cret");
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var unauthorized = await http.GetAsync("/api/sessions");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var withToken = new HttpRequestMessage(HttpMethod.Get, "/api/sessions");
        withToken.Headers.Add("X-NHibernaut-Token", "s3cret");
        var ok = await http.SendAsync(withToken);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Stream_endpoint_pushes_new_sealed_session()
    {
        using var db = new SqliteTestDatabase();
        using var sf = db.BuildProfiledSessionFactory();

        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stream = await http.GetStreamAsync("/api/stream");
        using var reader = new System.IO.StreamReader(stream);

        // Headers received => the server handler has subscribed. Now trigger a sealed session.
        GenerateSession(sf);

        string? dataLine = null;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) break;
            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                dataLine = line.Substring("data:".Length).Trim();
                break;
            }
        }

        Assert.NotNull(dataLine);
        using var doc = JsonDocument.Parse(dataLine!);
        Assert.True(doc.RootElement.TryGetProperty("id", out _));
    }
}
