using System;

namespace NHibernaut.Core;

/// <summary>Context passed to a parameter redactor so it can decide how to mask a value.</summary>
public sealed class ParamContext
{
    public string? ParameterName { get; init; }
    public object? Value { get; init; }
    public string? DbType { get; init; }
    public string? Sql { get; init; }
}

/// <summary>Settings for the self-hosted dashboard server.</summary>
public sealed class DashboardOptions
{
    /// <summary>Start the in-process dashboard server automatically when capture is enabled.</summary>
    public bool AutoStartServer { get; set; } = false;

    /// <summary>Bind address; loopback by default to avoid exposing SQL/parameter values.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5005;

    /// <summary>Required when <see cref="BindAddress"/> is not loopback; enforced on every request.</summary>
    public string? AuthToken { get; set; }

    /// <summary>Allow the dashboard in production. Off by default — the dashboard exposes sensitive data.</summary>
    public bool EnabledInProduction { get; set; } = false;

    /// <summary>Optional authorization hook (Tier C). The argument is the host-specific request context.</summary>
    public Func<object?, bool>? Authorize { get; set; }

    /// <summary>URI scheme for click-to-source deep links (e.g. <c>vscode://file/...</c>).</summary>
    public string EditorLinkScheme { get; set; } = "vscode";
}

/// <summary>
/// All NHibernaut configuration: per-alert thresholds, capture/redaction options, retention, and the
/// dashboard settings. Construct, mutate via <c>EnableNHibernaut(o =&gt; ...)</c>, and read from
/// <see cref="NHibernautRuntime.Options"/>.
/// </summary>
public sealed class NHibernautOptions
{
    // ---- alert thresholds (Spec section 7) ----

    /// <summary>N+1: at least this many statements sharing a NormalizedSql, or collection-inits of the same role.</summary>
    public int NPlusOneThreshold { get; set; } = 10;

    /// <summary>TooManyQueries: session statement count above this.</summary>
    public int TooManyQueriesThreshold { get; set; } = 50;

    /// <summary>UnboundedResultSet: an unlimited SELECT returning more than this many rows.</summary>
    public int UnboundedResultSetRowThreshold { get; set; } = 100;

    /// <summary>TooManyRows: a single statement returning more than this many rows.</summary>
    public int TooManyRowsThreshold { get; set; } = 1000;

    /// <summary>TooManyJoins: JOIN count in one statement above this.</summary>
    public int TooManyJoinsThreshold { get; set; } = 5;

    /// <summary>SlowQuery: statement duration (ms) above this.</summary>
    public int SlowQueryMs { get; set; } = 200;

    /// <summary>TooManyWrites: session write count above this.</summary>
    public int TooManyWritesThreshold { get; set; } = 50;

    // ---- capture options ----

    /// <summary>Capture bound parameter values. Disable for PII-sensitive production.</summary>
    public bool CaptureParameterValues { get; set; } = true;

    /// <summary>Optional redaction hook applied to each captured parameter value.</summary>
    public Func<ParamContext, object?>? ParameterRedactor { get; set; }

    /// <summary>Capture filtered stack traces (expensive). Defaults on only in a Development environment.</summary>
    public bool CaptureStackTraces { get; set; } = IsDevelopmentEnvironment();

    /// <summary>Namespace prefixes whose frames are stripped from captured stack traces.</summary>
    public string[] StackTraceNamespaceFilter { get; set; } =
        { "NHibernate.", "System.Data.", "NHibernaut." };

    /// <summary>Upper bound on rows counted per reader (prevents pathological counting on huge results).</summary>
    public int MaxCapturedRows { get; set; } = 10_000;

    /// <summary>Retain at most this many sessions in the in-memory store.</summary>
    public int RetentionSessionCount { get; set; } = 200;

    /// <summary>Drop sessions older than this.</summary>
    public TimeSpan RetentionMaxAge { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Fraction of sessions to profile (0..1). 1.0 = all.</summary>
    public double SamplingRate { get; set; } = 1.0;

    /// <summary>Dashboard server settings.</summary>
    public DashboardOptions Dashboard { get; set; } = new();

    /// <summary>True when ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT is "Development".</summary>
    public static bool IsDevelopmentEnvironment()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
