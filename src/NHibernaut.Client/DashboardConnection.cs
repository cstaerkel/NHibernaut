using System;

namespace NHibernaut.Client;

public enum DashboardMode { Remote, Embedded }

/// <summary>How the app reaches profiling data: a remote dashboard URL, or an in-process collector.</summary>
public sealed record DashboardConnection
{
    public DashboardMode Mode { get; init; } = DashboardMode.Remote;
    public string Url { get; init; } = "http://127.0.0.1:5005";
    public string? Token { get; init; }
    public string BindAddress { get; init; } = "127.0.0.1";
    public int Port { get; init; } = 5005;

    public static DashboardConnection Remote(string url, string? token = null) =>
        new() { Mode = DashboardMode.Remote, Url = url, Token = token };

    public static DashboardConnection Embedded(string bind = "127.0.0.1", int port = 5005, string? token = null) =>
        new() { Mode = DashboardMode.Embedded, BindAddress = bind, Port = port, Token = token };
}
