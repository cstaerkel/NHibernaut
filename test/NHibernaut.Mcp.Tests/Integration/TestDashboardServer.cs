using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NHibernaut.Core;
using NHibernaut.Mcp.Tests.Infrastructure;
using NHibernaut.Server;

namespace NHibernaut.Mcp.Tests.Integration;

public sealed class TestDashboardServer : IDisposable
{
    private readonly NHibernautServer _server;
    private bool _disposed;

    private TestDashboardServer(string? token)
    {
        var options = new NHibernautOptions
        {
            Dashboard =
            {
                BindAddress = "127.0.0.1",
                Port = FreeLoopbackPort(),
                AuthToken = token,
            },
        };

        NHibernautRuntime.Store.Clear();
        _server = NHibernautServer.Start(options);
        Url = $"http://127.0.0.1:{options.Dashboard.Port}";
    }

    public string Url { get; }

    public static TestDashboardServer StartSeeded(string? token = null)
    {
        var server = new TestDashboardServer(token);
        server.Seed(SampleDetail());

        return server;
    }

    public void Seed(SessionDetailDto detail)
        => DashboardApi.Ingest(detail);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _server.Dispose();
        NHibernautServer.Stop();
        NHibernautRuntime.Store.Clear();
    }

    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static SessionDetailDto SampleDetail()
    {
        var startedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        var endedAt = startedAt.AddSeconds(1);
        var statementId = SampleProfilerData.StatementA1Id.ToString();

        return new SessionDetailDto(
            new SessionSummaryDto(
                SampleProfilerData.SessionAId.ToString(),
                startedAt,
                endedAt,
                true,
                1,
                12.5,
                3,
                0,
                1,
                "Warning",
                1),
            [
                new StatementDto(
                    statementId,
                    SampleProfilerData.SessionAId.ToString(),
                    "SELECT * FROM widgets WHERE id = ?",
                    "SELECT * FROM widgets WHERE id = ?",
                    "Select",
                    startedAt,
                    12.5,
                    null,
                    3,
                    null,
                    null,
                    1,
                    1,
                    [new ParamDto("p0", "Int32", "42", 4, "Input")]),
            ],
            [
                new ConnectionDto(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1").ToString(),
                    startedAt,
                    endedAt,
                    [statementId]),
            ],
            [
                new TransactionDto(
                    Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddd01").ToString(),
                    startedAt,
                    endedAt,
                    "Commit"),
            ],
            [new EntityLoadDto("Widget", "42", statementId)],
            [],
            [new CollectionInitDto("Widget.Tags", statementId)],
            [
                new AlertDto(
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeee1").ToString(),
                    "SelectNPlusOne",
                    "Warning",
                    "N+1",
                    "Repeated query shape.",
                    "Use fetch joins or batch fetching.",
                    [statementId]),
            ],
            new Dictionary<string, int> { ["Widget"] = 1 });
    }
}
