using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NHibernaut.App.Composition;

namespace NHibernaut.App;

internal static class Program
{
    public static IHost Host { get; private set; } = default!;

    [STAThread]
    public static void Main(string[] args)
    {
        var verbose = args.Contains("--verbose");
        Host = HostBuilderFactory.Build(args, verbose ? LogLevel.Debug : LogLevel.Information);
        var log = Host.Services.GetRequiredService<ILogger<App>>();

        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.LogCritical(e.ExceptionObject as Exception, "Unhandled AppDomain exception");
        TaskScheduler.UnobservedTaskException += (_, e) => { log.LogError(e.Exception, "Unobserved task exception"); e.SetObserved(); };

        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { log.LogCritical(ex, "Fatal: app terminated"); throw; }
        finally { Host.Dispose(); }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
