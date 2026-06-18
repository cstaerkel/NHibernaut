using System;
using System.Net.Http;
using System.Threading.Tasks;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.Client;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public class HttpDashboardClientTests
{
    private static SessionDetailDto SampleSession(Guid id) => Infrastructure.SampleData.Session(id);

    [Fact]
    public async Task GetSessionsAsync_returns_seeded_sessions()
    {
        using var server = new TestDashboardServer();
        var id = Guid.NewGuid();
        server.Seed(SampleSession(id));

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        var sessions = await client.GetSessionsAsync();

        Assert.Contains(sessions, s => s.Id == id.ToString());
    }

    [Fact]
    public async Task GetSessionDetailAsync_unknown_id_returns_null()
    {
        using var server = new TestDashboardServer();

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        var detail = await client.GetSessionDetailAsync(Guid.NewGuid());

        Assert.Null(detail);
    }

    [Fact]
    public async Task GetSessionDetailAsync_returns_seeded_detail()
    {
        using var server = new TestDashboardServer();
        var id = Guid.NewGuid();
        server.Seed(SampleSession(id));

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        var detail = await client.GetSessionDetailAsync(id);

        Assert.NotNull(detail);
        Assert.Equal(id.ToString(), detail!.Summary.Id);
    }

    [Fact]
    public async Task ClearAsync_empties_store()
    {
        using var server = new TestDashboardServer();
        server.Seed(SampleSession(Guid.NewGuid()));

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        await client.ClearAsync();
        var sessions = await client.GetSessionsAsync();

        Assert.Empty(sessions);
    }

    [Fact]
    public async Task GetAggregateAsync_returns_non_null_list()
    {
        using var server = new TestDashboardServer();

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        var aggregate = await client.GetAggregateAsync();

        Assert.NotNull(aggregate);
    }

    [Fact]
    public async Task GetAlertsAsync_returns_non_null_list()
    {
        using var server = new TestDashboardServer();

        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        var alerts = await client.GetAlertsAsync();

        Assert.NotNull(alerts);
    }

    [Fact]
    public async Task Token_required_when_server_has_token()
    {
        using var server = new TestDashboardServer(token: "secret");

        // Wrong token → 401
        await using var wrongClient = new HttpDashboardClient(DashboardConnection.Remote(server.Url, token: "wrong"));
        await Assert.ThrowsAsync<HttpRequestException>(() => wrongClient.GetSessionsAsync());

        // Correct token → success
        await using var correctClient = new HttpDashboardClient(DashboardConnection.Remote(server.Url, token: "secret"));
        var sessions = await correctClient.GetSessionsAsync();
        Assert.NotNull(sessions);
    }

    [Fact]
    public async Task StreamSessionsAsync_pushes_newly_sealed_session()
    {
        using var server = new TestDashboardServer();
        await using var client = new HttpDashboardClient(DashboardConnection.Remote(server.Url));
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));

        var id = Guid.NewGuid();
        var received = new TaskCompletionSource<string>();
        var pump = Task.Run(async () =>
        {
            await foreach (var s in client.StreamSessionsAsync(cts.Token))
                if (s.Id == id.ToString()) { received.TrySetResult(s.Id); break; }
        }, cts.Token);

        await Task.Delay(300, cts.Token);          // let the SSE connection establish
        server.Seed(SampleSession(id));            // raises SessionSealed -> SSE frame

        var got = await received.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(id.ToString(), got);
        cts.Cancel();
    }
}
