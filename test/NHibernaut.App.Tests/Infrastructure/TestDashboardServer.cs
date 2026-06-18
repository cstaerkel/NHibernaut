using System;
using System.Net;
using System.Net.Sockets;
using NHibernaut.Core;
using NHibernaut.Server;

namespace NHibernaut.App.Tests.Infrastructure;

/// <summary>Starts a real NHibernautServer on a free loopback port; seed the store, then point a client at Url.</summary>
public sealed class TestDashboardServer : IDisposable
{
    public string Url { get; }

    public TestDashboardServer(string? token = null)
    {
        var port = FreePort();
        var options = new NHibernautOptions
        {
            Dashboard = { BindAddress = "127.0.0.1", Port = port, AuthToken = token }
        };
        // Clear shared static store so previous test sessions don't bleed in.
        NHibernautRuntime.Store.Clear();
        NHibernautServer.Start(options);
        Url = $"http://127.0.0.1:{port}";
    }

    public void Seed(SessionDetailDto detail) => DashboardApi.Ingest(detail);

    public static int FreeLoopbackPort()
    {
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private static int FreePort() => FreeLoopbackPort();

    public void Dispose() => NHibernautServer.Stop();
}
