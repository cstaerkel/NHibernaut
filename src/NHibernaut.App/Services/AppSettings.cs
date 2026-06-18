using Microsoft.Extensions.Logging;
using NHibernaut.Client;

namespace NHibernaut.App.Services;

public sealed class AppSettings
{
    public DashboardConnection LastConnection { get; set; } = DashboardConnection.Remote("http://127.0.0.1:5005");
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>UI colour theme. Defaults to <see cref="AppTheme.Dark"/> to preserve prior behaviour.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
}

/// <summary>The desktop app's colour theme; serialized by name via the registered enum converter.</summary>
public enum AppTheme
{
    Dark,
    Light,
}
