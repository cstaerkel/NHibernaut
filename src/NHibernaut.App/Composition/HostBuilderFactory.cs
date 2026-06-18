using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NHibernaut.App.Logging;
using NHibernaut.App.Services;
using NHibernaut.Client;

namespace NHibernaut.App.Composition;

public static class HostBuilderFactory
{
    public static IHost Build(string[] args, LogLevel fileLevel)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        builder.Logging.AddDebug();
        builder.Logging.AddProvider(new FileLoggerProvider(new FileLoggerOptions { MinLevel = fileLevel }));
        builder.Logging.SetMinimumLevel(LogLevel.Trace);

        builder.Services.AddHttpClient("dashboard");
        builder.Services.AddSingleton<IDashboardClientFactory, DashboardClientFactory>();
        builder.Services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        builder.Services.AddSingleton<ISettingsService, JsonSettingsService>();
        builder.Services.AddSingleton<IThemeService, AvaloniaThemeService>();
        builder.Services.AddSingleton<NHibernaut.App.Services.LiveFeedService>();
        builder.Services.AddSingleton<NHibernaut.App.Services.EditorSchemeProvider>();
        builder.Services.AddSingleton<NHibernaut.App.Services.IEditorLinkService, NHibernaut.App.Services.EditorLinkService>();
        builder.Services.AddTransient<NHibernaut.App.ViewModels.SessionDetailViewModel>();
        builder.Services.AddSingleton<Func<NHibernaut.App.ViewModels.SessionDetailViewModel>>(
            sp => () => sp.GetRequiredService<NHibernaut.App.ViewModels.SessionDetailViewModel>());
        builder.Services.AddSingleton<NHibernaut.App.ViewModels.SessionsViewModel>();
        builder.Services.AddSingleton<NHibernaut.App.ViewModels.AggregateViewModel>();
        builder.Services.AddSingleton<NHibernaut.App.ViewModels.CompareViewModel>();
        builder.Services.AddSingleton<NHibernaut.App.ViewModels.ConnectionViewModel>();
        builder.Services.AddSingleton<NHibernaut.App.ViewModels.MainWindowViewModel>();

        return builder.Build();
    }
}
