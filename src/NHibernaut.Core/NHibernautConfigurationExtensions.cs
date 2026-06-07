using System;
using NHibernate.Cfg;
using NHibernate.Connection;
using NHibernate.Event;
using NHibernaut.Core.Capture;
using NHEnvironment = NHibernate.Cfg.Environment;

namespace NHibernaut.Core;

/// <summary>
/// Entry point for enabling NHibernaut on an NHibernate <see cref="Configuration"/>.
/// </summary>
public static class NHibernautConfigurationExtensions
{
    /// <summary>
    /// Enables host-agnostic capture on the given configuration:
    /// stashes the host's current connection provider and redirects <c>connection.provider</c> to
    /// <see cref="ProfilingConnectionProvider"/> so every connection (and its commands) is profiled.
    /// </summary>
    public static Configuration EnableNHibernaut(this Configuration cfg, Action<NHibernautOptions>? configure = null)
    {
        if (cfg is null) throw new ArgumentNullException(nameof(cfg));

        var options = new NHibernautOptions();
        configure?.Invoke(options);
        NHibernautRuntime.Options = options;
        NHibernautRuntime.TierAActive = true; // full-fidelity capture on → Tier B logging baseline stands down

        var store = new Storage.InMemoryProfilerStore(options);
        // Finalize analysis when a session is sealed (idempotent; safe to re-run on re-seal).
        store.SessionSealed += session =>
            NHibernautRuntime.SafeExecute(() => NHibernautRuntime.Analysis.Analyze(session, options));
        NHibernautRuntime.Store = store;

        // Stash the host's current provider (default DriverConnectionProvider) and swap in ours.
        var innerProvider = cfg.Properties.TryGetValue(NHEnvironment.ConnectionProvider, out var existingProvider)
                            && !string.IsNullOrWhiteSpace(existingProvider)
            ? existingProvider
            : typeof(DriverConnectionProvider).AssemblyQualifiedName!;

        cfg.SetProperty(ProfilingConnectionProvider.InnerProviderPropertyKey, innerProvider);
        cfg.SetProperty(NHEnvironment.ConnectionProvider, typeof(ProfilingConnectionProvider).AssemblyQualifiedName);

        // Swap the driver too: NHibernate creates commands via ConnectionProvider.Driver.GenerateCommand,
        // so command capture requires wrapping the driver (the connection swap alone is not enough).
        if (cfg.Properties.TryGetValue(NHEnvironment.ConnectionDriver, out var existingDriver)
            && !string.IsNullOrWhiteSpace(existingDriver))
        {
            cfg.SetProperty(ProfilingDriver.InnerDriverPropertyKey, existingDriver);
            cfg.SetProperty(NHEnvironment.ConnectionDriver, typeof(ProfilingDriver).AssemblyQualifiedName);
        }
        // else: no explicit connection.driver_class (dialect default). Resolving the dialect's default
        // driver is handled in M2; without it command capture won't fire, but the host is unaffected.

        // Append object-level capture listeners. One instance serves all listener types.
        // NOTE: the collection-initialize hook registers under ListenerType.LoadCollection
        // (NHibernate has no "InitializeCollection" enum value despite the interface name).
        // AppendListeners casts the array to the interface type for the given ListenerType, so each
        // array must be typed to that exact interface (not object[]).
        var listener = new NHibernautEventListener();
        cfg.AppendListeners(ListenerType.PostLoad, new IPostLoadEventListener[] { listener });
        cfg.AppendListeners(ListenerType.PostInsert, new IPostInsertEventListener[] { listener });
        cfg.AppendListeners(ListenerType.PostUpdate, new IPostUpdateEventListener[] { listener });
        cfg.AppendListeners(ListenerType.PostDelete, new IPostDeleteEventListener[] { listener });
        cfg.AppendListeners(ListenerType.LoadCollection, new IInitializeCollectionEventListener[] { listener });

        return cfg;
    }
}
