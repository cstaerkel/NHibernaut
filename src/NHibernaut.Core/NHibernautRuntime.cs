using System;
using NHibernaut.Core.Analysis;
using NHibernaut.Core.Storage;

namespace NHibernaut.Core;

/// <summary>
/// Static entry point all capture code routes through. Holds options and a diagnostic error
/// channel; provides fail-safe wrappers so internal errors never surface to the host.
/// </summary>
public static class NHibernautRuntime
{
    /// <summary>Active options (set by EnableNHibernaut). Never null.</summary>
    public static NHibernautOptions Options { get; internal set; } = new NHibernautOptions();

    /// <summary>Active session store. Defaults to an in-memory store; replaced by EnableNHibernaut.</summary>
    public static IProfilerStore Store { get; internal set; } = new InMemoryProfilerStore();

    /// <summary>Analysis pipeline run when a session is sealed.</summary>
    public static AnalysisPipeline Analysis { get; internal set; } = new AnalysisPipeline();

    /// <summary>
    /// True once Tier A (EnableNHibernaut) has run. The Tier B logging baseline stands down while this
    /// is set, so full-fidelity capture and the logging baseline never double-count the same SQL.
    /// </summary>
    public static bool TierAActive { get; internal set; }

    /// <summary>Diagnostic channel for swallowed internal errors. Never throws into the host.</summary>
    public static event Action<Exception>? InternalError;

    /// <summary>
    /// Whether a session should be profiled, per <see cref="NHibernautOptions.SamplingRate"/>. The
    /// decision is stable per session id (derived from its hash) so every capture point agrees for the
    /// lifetime of the session.
    /// </summary>
    public static bool IsSampled(Guid sessionId)
    {
        var rate = Options.SamplingRate;
        if (rate >= 1.0) return true;
        if (rate <= 0.0) return false;
        var bucket = (uint)sessionId.GetHashCode() / (double)uint.MaxValue;
        return bucket < rate;
    }

    // ---- fail-safe helpers ----

    /// <summary>Report an internal error to the diagnostic channel without ever throwing.</summary>
    public static void ReportInternalError(Exception ex)
    {
        var handler = InternalError;
        if (handler is null) return;
        try { handler(ex); }
        catch { /* a misbehaving subscriber must not surface to the host either */ }
    }

    /// <summary>Run a capture action, swallowing and reporting any exception.</summary>
    internal static void SafeExecute(Action action)
    {
        try { action(); }
        catch (Exception ex) { ReportInternalError(ex); }
    }

    /// <summary>Run a capture function, swallowing and reporting any exception, returning a fallback.</summary>
    internal static T SafeExecute<T>(Func<T> func, T fallback)
    {
        try { return func(); }
        catch (Exception ex) { ReportInternalError(ex); return fallback; }
    }
}
