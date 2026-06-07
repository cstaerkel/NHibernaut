using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernaut.Core;

namespace NHibernaut.AspNetCore;

/// <summary>Pipeline wiring for the optional ASP.NET Core (Tier C) integration.</summary>
public static class NHibernautApplicationBuilderExtensions
{
    /// <summary>Adds per-request scoping plus the Server-Timing / X-NHibernaut-RequestId headers.</summary>
    public static IApplicationBuilder UseNHibernaut(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<NHibernautOptions>() ?? NHibernautRuntime.Options;
        return app.UseMiddleware<NHibernautMiddleware>(options);
    }

    /// <summary>Mounts the dashboard + JSON API + SSE under <paramref name="path"/> on the host server.</summary>
    public static IApplicationBuilder MapNHibernaut(this IApplicationBuilder app, string path = "/nhibernaut")
    {
        var options = app.ApplicationServices.GetService<NHibernautOptions>() ?? NHibernautRuntime.Options;
        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();

        return app.Map(path, branch => branch.Run(ctx => DashboardEndpoints.HandleAsync(ctx, options, env, path)));
    }
}
