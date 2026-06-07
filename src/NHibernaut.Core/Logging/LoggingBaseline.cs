using System;
using System.Runtime.CompilerServices;
using System.Threading;
using NHibernate;

namespace NHibernaut.Core.Logging;

/// <summary>
/// Installs the Tier B logging baseline on assembly load via a module initializer.
/// </summary>
/// <remarks>
/// A module initializer runs only once the assembly is actually loaded by the runtime. A purely
/// transitive reference that no code ever touches may not trigger it — calling <c>EnableNHibernaut</c>
/// or <c>NHibernautServer.Start</c> guarantees the assembly loads (and Tier A then supersedes this).
/// </remarks>
internal static class LoggingBaseline
{
    private static int _installed;

    // CA2255: a module initializer in a library is exactly the documented Tier B "zero-touch" mechanism
    // (Spec section 4) — install the logging baseline when the assembly loads. Intentional.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255",
        Justification = "Tier B zero-touch baseline installs on assembly load by design.")]
    [ModuleInitializer]
    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;

        // Fail-safe: a misbehaving install must never break host startup.
        try
        {
            NHibernateLogger.SetLoggersFactory(new NHibernautLoggerFactory());
        }
        catch (Exception ex)
        {
            NHibernautRuntime.ReportInternalError(ex);
        }
    }
}
