using System;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NHibernaut.App.Services;
using NHibernaut.Client;

namespace NHibernaut.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // Material Design Icons: "white-balance-sunny" and "weather-night" (filled, 24×24 viewbox).
    private const string SunIcon = "M3.55,18.54L4.96,19.95L6.76,18.16L5.34,16.74M11,22.45C11.32,22.45 13,22.45 13,22.45V19.5H11M12,5.5A6,6 0 0,0 6,11.5A6,6 0 0,0 12,17.5A6,6 0 0,0 18,11.5A6,6 0 0,0 12,5.5M20,12.5H23V10.5H20M17.24,18.16L19.04,19.95L20.45,18.54L18.66,16.74M20.45,4.46L19.04,3.05L17.24,4.84L18.66,6.26M13,0.55H11V3.5H13M4.96,3.05L3.55,4.46L5.34,6.26L6.76,4.84M1,10.5H4V12.5H1Z";
    private const string MoonIcon = "M17.75,4.09L15.22,6.03L16.13,9.09L13.5,7.28L10.87,9.09L11.78,6.03L9.25,4.09L12.44,4L13.5,1L14.56,4L17.75,4.09M21.25,11L19.61,12.25L20.2,14.23L18.5,13.06L16.8,14.23L17.39,12.25L15.75,11L17.81,10.95L18.5,9L19.19,10.95L21.25,11M18.97,15.95C19.8,15.87 20.69,17.05 20.16,17.8C19.84,18.25 19.5,18.67 19.08,19.07C15.17,23 8.84,23 4.94,19.07C1.03,15.17 1.03,8.83 4.94,4.93C5.34,4.53 5.76,4.17 6.21,3.85C6.96,3.32 8.14,4.21 8.06,5.04C7.79,7.9 8.75,10.87 10.95,13.06C13.14,15.26 16.1,16.22 18.97,15.95Z";

    private readonly LiveFeedService _feed;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly EditorSchemeProvider _scheme;
    private IDashboardClient? _client;
    private string _baseConnectionLabel = "disconnected";

    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _connectionLabel = "disconnected";

    /// <summary>True when the Light theme is active. Drives the toggle button's icon and tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThemeIcon))]
    [NotifyPropertyChangedFor(nameof(ThemeToggleTip))]
    private bool _isLightTheme;

    /// <summary>Icon shown on the toggle: the theme you'll switch *to* (sun while dark, moon while light).</summary>
    public Geometry ThemeIcon => Geometry.Parse(IsLightTheme ? MoonIcon : SunIcon);

    public string ThemeToggleTip => IsLightTheme ? "Switch to dark theme" : "Switch to light theme";

    public ConnectionViewModel Connection { get; }
    public SessionsViewModel Sessions { get; }
    public AggregateViewModel Aggregate { get; }
    public CompareViewModel Compare { get; }

    public MainWindowViewModel(LiveFeedService feed, ISettingsService settings, SessionsViewModel sessions, EditorSchemeProvider scheme, AggregateViewModel aggregate, CompareViewModel compare, ConnectionViewModel connection, IThemeService theme)
    {
        _feed = feed; _settings = settings; _scheme = scheme; _theme = theme;
        Sessions = sessions; Aggregate = aggregate; Compare = compare;
        Connection = connection;
        CurrentPage = Sessions;
        _feed.SessionReceived += Sessions.ApplyLiveSession;
        _feed.StatusChanged += OnFeedStatusChanged;
        Connection.Connected += OnConnectedAsync;
        Connection.Disconnecting += OnDisconnectingAsync;
    }

    public async Task InitializeAsync()
    {
        // Settings are loaded and the theme applied in App.OnFrameworkInitializationCompleted
        // (before the window paints). Reflect the persisted theme into the toggle's state so the
        // toolbar icon matches; the OnIsLightThemeChanged apply is idempotent, and no save occurs.
        IsLightTheme = _settings.Current.Theme == AppTheme.Light;
        await Connection.ConnectAsync(_settings.Current.LastConnection);
    }

    /// <summary>Apply-only: reflects the flag into the live application theme. Persistence happens in <see cref="ToggleThemeAsync"/>.</summary>
    partial void OnIsLightThemeChanged(bool value) =>
        _theme.Apply(value ? AppTheme.Light : AppTheme.Dark);

    /// <summary>Flip the theme, then persist the choice so it survives a restart.</summary>
    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        IsLightTheme = !IsLightTheme;
        _settings.Current.Theme = IsLightTheme ? AppTheme.Light : AppTheme.Dark;
        try { await _settings.SaveAsync(); } catch { /* best-effort persistence */ }
    }

    /// <summary>Reflects the live feed's pump state into the connection label (raised on the UI thread).</summary>
    private void OnFeedStatusChanged(string status)
    {
        ConnectionLabel = status == "reconnecting"
            ? _baseConnectionLabel + " (reconnecting…)"
            : _baseConnectionLabel;
    }

    private async Task OnDisconnectingAsync()
    {
        _feed.Stop();
        if (_client is not null) { try { await _client.DisposeAsync(); } catch { } _client = null; }
        // If the next connect fails, ConnectionViewModel only sets Status — so reset the shell here,
        // else the top bar and pages keep showing the previous (now dead) connection's data.
        _baseConnectionLabel = "disconnected";
        ConnectionLabel = "disconnected";
        ResetPagesLocal();
    }

    private async Task OnConnectedAsync(IDashboardClient client)
    {
        _client = client;
        try
        {
            _scheme.Scheme = (await client.GetConfigAsync()).EditorLinkScheme;
            await Sessions.LoadAsync(client);
            await Aggregate.LoadAsync(client);
            await Compare.LoadAsync(client);
        }
        catch
        {
            // The just-created client failed to load. Tear it down (in embedded mode this stops the
            // in-process NHibernautServer) so we don't leave a running server behind an "error" UI.
            _feed.Stop();
            try { await client.DisposeAsync(); } catch { }
            if (ReferenceEquals(_client, client)) _client = null;
            throw; // let ConnectionViewModel.ConnectAsync surface "error: …"
        }

        // Set the label BEFORE Start(): Start() may synchronously raise StatusChanged("connected").
        _baseConnectionLabel = client.Connection.Mode == DashboardMode.Embedded
            ? $"embedded :{client.Connection.Port}" : client.Connection.Url;
        ConnectionLabel = _baseConnectionLabel;
        _feed.Start(client);
        _settings.Current.LastConnection = client.Connection;
        try { await _settings.SaveAsync(); } catch { }
        CurrentPage = Sessions;
    }

    /// <summary>Clear all profiling data on the backend, then reset every page's in-memory view.</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_client is not null)
        {
            try { await _client.ClearAsync(); } catch { /* best-effort; still clear the UI */ }
        }
        ResetPagesLocal();
    }

    /// <summary>Reset every page's in-memory view (no backend call). Shared by Clear-all and disconnect.</summary>
    private void ResetPagesLocal()
    {
        Sessions.ClearLocal();
        Aggregate.Rows.Clear();
        Compare.Clear();
    }

    [RelayCommand] private void ShowSessions() => CurrentPage = Sessions;

    [RelayCommand]
    private void ShowAggregate()
    {
        CurrentPage = Aggregate;
        if (_client is { } client) _ = RefreshAggregateAsync(client);
    }

    [RelayCommand]
    private void ShowCompare()
    {
        CurrentPage = Compare;
        if (_client is { } client) _ = RefreshCompareAsync(client);
    }

    [RelayCommand] private void ShowConnection() => CurrentPage = Connection;

    private async Task RefreshAggregateAsync(IDashboardClient client)
    {
        try { await Aggregate.LoadAsync(client); } catch { /* navigation refresh is best-effort */ }
    }

    private async Task RefreshCompareAsync(IDashboardClient client)
    {
        try { await Compare.RefreshOptionsAsync(client); } catch { /* navigation refresh is best-effort */ }
    }
}
