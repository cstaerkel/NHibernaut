using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NHibernaut.AspNetCore;

[assembly: HostingStartup(typeof(NHibernautHostingStartup))]

namespace NHibernaut.AspNetCore;

/// <summary>
/// Env-var auto-wire: when <c>NHIBERNAUT_ENABLED</c> is truthy, registers NHibernaut services and inserts
/// the middleware + dashboard mount without any code change in the host. Enabled via
/// <c>ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=NHibernaut.AspNetCore</c>.
/// </summary>
public sealed class NHibernautHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        var enabled = Environment.GetEnvironmentVariable("NHIBERNAUT_ENABLED");
        if (!IsTruthy(enabled)) return;

        builder.ConfigureServices(services =>
        {
            services.AddNHibernaut();
            services.AddSingleton<IStartupFilter, NHibernautStartupFilter>();
        });
    }

    private static bool IsTruthy(string? value)
        => value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    private sealed class NHibernautStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseNHibernaut();
            app.MapNHibernaut();
            next(app);
        };
    }
}
