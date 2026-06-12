using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Core.Storage;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.Tests;

// Phase 2: remote ingestion — a forwarded, sealed session round-trips through the wire DTO and lands
// in the store via POST /api/ingest, then shows up through the normal dashboard endpoints.
public class M11_IngestTests
{
    // ---- Core: InsertSession ----

    [Fact]
    public void InsertSession_stores_session_and_raises_sealed()
    {
        var store = new InMemoryProfilerStore();
        ProfiledSession? raised = null;
        store.SessionSealed += s => raised = s;

        var session = SampleSession();
        store.InsertSession(session);

        Assert.Same(session, store.GetSession(session.Id));
        Assert.Contains(store.GetRecentSessions(10), s => s.Id == session.Id);
        Assert.Same(session, raised);
    }

    [Fact]
    public void InsertSession_upserts_by_id()
    {
        var store = new InMemoryProfilerStore();
        var id = Guid.NewGuid();
        store.InsertSession(SampleSession(id));
        store.InsertSession(SampleSession(id));
        Assert.Equal(1, store.Count);
    }

    // A custom store written before remote ingestion existed still compiles (InsertSession is a default
    // interface method), and the default throws if ingest is attempted against a store that didn't opt in.
    [Fact]
    public void Custom_store_without_InsertSession_uses_the_throwing_default()
    {
        IProfilerStore store = new LegacyStore();
        Assert.Throws<NotSupportedException>(() => store.InsertSession(new ProfiledSession()));
    }

    private sealed class LegacyStore : IProfilerStore
    {
        public ProfiledSession GetOrCreateSession(Guid sessionId) => new() { Id = sessionId };
        public ProfiledSession? GetSession(Guid sessionId) => null;
        public IReadOnlyList<ProfiledSession> GetRecentSessions(int take) => Array.Empty<ProfiledSession>();
        public void SealSession(Guid sessionId) { }
        public void Clear() { }
        public int Count => 0;
        public event Action<ProfiledSession>? SessionSealed { add { } remove { } }
        // InsertSession intentionally omitted — relies on the default interface implementation.
    }

    // ---- Server: wire round-trip + reconstruct ----

    [Fact]
    public void Session_round_trips_through_the_wire_dto()
    {
        var original = SampleSession();
        var json = JsonSerializer.Serialize(DtoMapper.ToDetail(original), NHibernautServer.JsonOptions);
        var back = JsonSerializer.Deserialize<SessionDetailDto>(json, NHibernautServer.JsonOptions);
        Assert.NotNull(back);

        var rebuilt = SessionReconstructor.FromDetail(back!);

        Assert.Equal(original.Id, rebuilt.Id);
        Assert.True(rebuilt.IsSealed);
        Assert.Equal(original.Statements.Count, rebuilt.Statements.Count);
        Assert.Equal(original.Statements[0].Sql, rebuilt.Statements[0].Sql);
        Assert.Equal(original.Statements[0].Kind, rebuilt.Statements[0].Kind);
        Assert.Equal(original.Statements[0].RowsRead, rebuilt.Statements[0].RowsRead);
        Assert.Equal(original.Statements[0].Parameters[0].Name, rebuilt.Statements[0].Parameters[0].Name);
        Assert.Equal("42", rebuilt.Statements[0].Parameters[0].Value);
        Assert.Equal(original.Writes[0].Kind, rebuilt.Writes[0].Kind);
        Assert.Equal(original.EntityLoads[0].EntityType, rebuilt.EntityLoads[0].EntityType);
        Assert.Equal(original.Alerts[0].Severity, rebuilt.Alerts[0].Severity);
        Assert.Equal(original.Alerts[0].Type, rebuilt.Alerts[0].Type);
        // Connections / transactions / collection-inits exercise ToConnection (Guid.Parse over the
        // StatementIds list), ToTransaction (enum), and the CollectionInit path.
        Assert.Equal(original.Connections[0].StatementIds[0], rebuilt.Connections[0].StatementIds[0]);
        Assert.Equal(original.Transactions[0].Outcome, rebuilt.Transactions[0].Outcome);
        Assert.Equal(original.CollectionInits[0].Role, rebuilt.CollectionInits[0].Role);
    }

    // ---- Server: end-to-end POST /api/ingest ----

    [Fact]
    public async Task Ingest_endpoint_accepts_a_forwarded_session_and_serves_it()
    {
        var original = SampleSession();
        var json = JsonSerializer.Serialize(DtoMapper.ToDetail(original), NHibernautServer.JsonOptions);

        var options = LoopbackOptions();
        NHibernautRuntime.Store.Clear();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var resp = await http.PostAsync("/api/ingest", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, resp.StatusCode);

        using var doc = JsonDocument.Parse(await http.GetStringAsync($"/api/sessions/{original.Id}"));
        Assert.True(doc.RootElement.TryGetProperty("statements", out var statements));
        Assert.True(statements.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Ingest_endpoint_rejects_malformed_payload()
    {
        var options = LoopbackOptions();
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var resp = await http.PostAsync("/api/ingest", new StringContent("{ not json", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Ingest_endpoint_enforces_the_auth_token()
    {
        var json = JsonSerializer.Serialize(DtoMapper.ToDetail(SampleSession()), NHibernautServer.JsonOptions);
        var options = LoopbackOptions(token: "s3cret");
        using var server = NHibernautServer.Start(options);
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };

        var noToken = await http.PostAsync("/api/ingest", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using var withToken = new HttpRequestMessage(HttpMethod.Post, "/api/ingest")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        withToken.Headers.Add("X-NHibernaut-Token", "s3cret");
        var ok = await http.SendAsync(withToken);
        Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
    }

    [Fact]
    public async Task RemoteForwarder_posts_sealed_sessions_to_the_server()
    {
        var options = LoopbackOptions();
        NHibernautRuntime.Store.Clear(); // the server reads the global store
        using var server = NHibernautServer.Start(options);

        // A separate "client app" store with a forwarder aimed at the server (no loop: distinct stores).
        var clientStore = new InMemoryProfilerStore();
        using var forwarder = new RemoteForwarder($"http://127.0.0.1:{options.Dashboard.Port}", token: null, clientStore);

        var session = SampleSession();
        clientStore.InsertSession(session); // raises SessionSealed -> forwarder POSTs asynchronously

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{options.Dashboard.Port}") };
        var arrived = false;
        for (var i = 0; i < 50 && !arrived; i++)
        {
            if ((await http.GetAsync($"/api/sessions/{session.Id}")).StatusCode == HttpStatusCode.OK) arrived = true;
            else await Task.Delay(100);
        }

        Assert.True(arrived, "forwarded session did not arrive at the server");
    }

    // ---- helpers ----

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

    private static ProfiledSession SampleSession(Guid? id = null)
    {
        var sid = id ?? Guid.NewGuid();
        var stmtId = Guid.NewGuid();
        var session = new ProfiledSession
        {
            Id = sid,
            StartedAt = DateTimeOffset.UtcNow,
            EndedAt = DateTimeOffset.UtcNow,
            IsSealed = true
        };
        session.ThreadIds.Add(1);

        var statement = new ProfiledStatement
        {
            Id = stmtId,
            SessionId = sid,
            Sql = "SELECT * FROM widgets WHERE id = ?",
            NormalizedSql = "SELECT * FROM widgets WHERE id = ?",
            Kind = StatementKind.Select,
            StartedAt = DateTimeOffset.UtcNow,
            DurationMs = 12.5,
            RowsRead = 3
        };
        statement.Parameters.Add(new ParamCapture { Name = "p0", DbType = "Int32", Value = "42", Size = 4 });
        session.Statements.Add(statement);

        session.Writes.Add(new EntityWrite { Kind = WriteKind.Insert, EntityType = "Widget", Id = "42", StatementId = stmtId });
        session.EntityLoads.Add(new EntityLoad { EntityType = "Widget", Id = "42", StatementId = stmtId });

        var connection = new ProfiledConnection { OpenedAt = DateTimeOffset.UtcNow, ClosedAt = DateTimeOffset.UtcNow };
        connection.StatementIds.Add(stmtId);
        session.Connections.Add(connection);
        session.Transactions.Add(new ProfiledTransaction { BeganAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow, Outcome = TransactionOutcome.Commit });
        session.CollectionInits.Add(new CollectionInit { Role = "Widget.Tags", StatementId = stmtId });

        session.Alerts.Add(new Alert { Type = "SelectNPlusOne", Severity = AlertSeverity.Warning, Title = "N+1", Description = "...", Suggestion = "fetch eagerly" });
        return session;
    }
}
