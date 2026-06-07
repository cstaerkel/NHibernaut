using System;
using Microsoft.Extensions.DependencyInjection;
using NHibernaut.Core;

namespace NHibernaut.AspNetCore;

/// <summary>DI registration for the optional ASP.NET Core (Tier C) integration.</summary>
public static class NHibernautServiceCollectionExtensions
{
    /// <summary>
    /// Registers NHibernaut options for the host. Capture itself is still wired via
    /// <c>cfg.EnableNHibernaut()</c> on the NHibernate configuration; this configures the dashboard,
    /// thresholds, and Tier C behavior.
    /// </summary>
    public static IServiceCollection AddNHibernaut(this IServiceCollection services, Action<NHibernautOptions>? configure = null)
    {
        var options = new NHibernautOptions();
        configure?.Invoke(options);
        NHibernautRuntime.Options = options;
        services.AddSingleton(options);
        return services;
    }
}
