using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NHibernaut.App.Logging;
using NHibernaut.App.Services;
using NHibernaut.App.ViewModels;
using NHibernaut.App.Views;

namespace NHibernaut.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var loggerFactory = Program.Host.Services.GetRequiredService<ILoggerFactory>();
        var avaloniaLogger = loggerFactory.CreateLogger("Avalonia");
        Avalonia.Logging.Logger.Sink = new AvaloniaLogSink(avaloniaLogger);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load settings and apply the persisted theme BEFORE the first window paints, so a
            // non-default (Light) choice doesn't flash from Dark on startup. The load is async;
            // run it off the UI thread to avoid a sync-over-async deadlock on Avalonia's context.
            var settings = Program.Host.Services.GetRequiredService<ISettingsService>();
            var theme = Program.Host.Services.GetRequiredService<IThemeService>();
            try { Task.Run(() => settings.LoadAsync()).GetAwaiter().GetResult(); } catch { /* defaults */ }
            theme.Apply(settings.Current.Theme);

            var vm = Program.Host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.MainWindow.Opened += async (_, _) => await vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
