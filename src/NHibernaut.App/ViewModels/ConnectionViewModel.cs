using System;
using System.Net;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NHibernaut.Client;

namespace NHibernaut.App.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IDashboardClientFactory _factory;

    [ObservableProperty] private bool _embedded;
    [ObservableProperty] private string _url = "http://127.0.0.1:5005";
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private string _bindAddress = "127.0.0.1";
    [ObservableProperty] private int _port = 5005;
    [ObservableProperty] private string _status = "disconnected";

    /// <summary>Raised with the newly-created client; the handler (MainWindowViewModel) rewires the UI.</summary>
    public event Func<IDashboardClient, Task>? Connected;

    /// <summary>Raised before the factory creates the new client; lets the handler tear down the previous client/server first.</summary>
    public event Func<Task>? Disconnecting;

    public ConnectionViewModel(IDashboardClientFactory factory) => _factory = factory;

    [RelayCommand]
    public async Task ConnectAsync(DashboardConnection? preset = null)
    {
        var conn = preset ?? (Embedded
            ? DashboardConnection.Embedded(BindAddress, Port, NullIfBlank(Token))
            : DashboardConnection.Remote(Url, NullIfBlank(Token)));
        Status = "connecting…";
        try
        {
            if (Disconnecting is not null) await Disconnecting.Invoke();   // tear down the previous client/server before creating the new one
            var client = await _factory.CreateAsync(conn);
            // reflect the active connection into the fields (esp. when a preset was used)
            Embedded = conn.Mode == DashboardMode.Embedded;
            Url = conn.Url; BindAddress = conn.BindAddress; Port = conn.Port; Token = conn.Token ?? "";
            Status = "connected";
            if (Connected is not null) await Connected.Invoke(client);
        }
        catch (Exception ex)
        {
            // Windows + non-loopback embedded bind: HTTP.sys rejects http://+:<port>/ with Access Denied
            // unless a URL ACL is reserved (or the app runs elevated). Surface the actionable fix.
            if (conn.Mode == DashboardMode.Embedded && !IsLoopback(conn.BindAddress) && IsAccessDenied(ex))
                Status = $"error: binding {conn.BindAddress}:{conn.Port} was denied. On Windows, reserve the URL once " +
                         $"(elevated): netsh http add urlacl url=http://+:{conn.Port}/ user=Everyone — or run the app elevated.";
            else
                Status = "error: " + ex.Message;
        }
    }

    private static bool IsAccessDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is HttpListenerException hle && hle.ErrorCode == 5) return true;
            if (e.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Mirrors NHibernautServer's loopback classification: 0.0.0.0 / :: bind to "+" and are NON-loopback.
    private static bool IsLoopback(string address)
    {
        if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var trimmed = address.Trim('[', ']');
        return IPAddress.TryParse(trimmed, out var ip) && IPAddress.IsLoopback(ip);
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
