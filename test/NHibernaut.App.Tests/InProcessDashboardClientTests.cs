using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.Client;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public class InProcessDashboardClientTests
{
    [Fact]
    public async Task Embedded_client_sees_ingested_sessions_and_streams_seal()
    {
        NHibernaut.Core.NHibernautRuntime.Store.Clear(); // isolation
        var port = TestDashboardServer.FreeLoopbackPort();
        await using var client = new InProcessDashboardClient(DashboardConnection.Embedded(port: port));
        await client.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var id = Guid.NewGuid();
        var received = new TaskCompletionSource<string>();
        var pump = Task.Run(async () =>
        {
            await foreach (var s in client.StreamSessionsAsync(cts.Token))
                if (s.Id == id.ToString()) { received.TrySetResult(s.Id); break; }
        }, cts.Token);

        await Task.Delay(100, cts.Token);
        DashboardApi.Ingest(SampleData.Session(id)); // raises SessionSealed

        Assert.Equal(id.ToString(), await received.Task.WaitAsync(TimeSpan.FromSeconds(8)));
        var sessions = await client.GetSessionsAsync();
        Assert.Contains(sessions, s => s.Id == id.ToString());
        cts.Cancel();
    }

    [Fact]
    public async Task Dispose_then_start_new_port_binds_a_live_listener()
    {
        // Regression: an embedded reconnect must dispose the old server BEFORE starting the new one,
        // otherwise the new StartAsync is a no-op (idempotent) and no listener binds the new port.
        NHibernaut.Core.NHibernautRuntime.Store.Clear();

        var portA = TestDashboardServer.FreeLoopbackPort();
        var clientA = new InProcessDashboardClient(DashboardConnection.Embedded(port: portA));
        await clientA.StartAsync();
        await clientA.DisposeAsync();   // tears down server A (sets _started=false so the next Start binds)

        var portB = TestDashboardServer.FreeLoopbackPort();
        await using var clientB = new InProcessDashboardClient(DashboardConnection.Embedded(port: portB));
        await clientB.StartAsync();

        // Server B must actually be listening on its new port.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var response = await http.GetAsync($"http://127.0.0.1:{portB}/api/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
