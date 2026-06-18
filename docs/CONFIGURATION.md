# NHibernaut — Configuration reference

Every knob NHibernaut exposes, with the **exact default from the code** (not the prose). For how
capture hooks in, see the [Architecture](ARCHITECTURE.md); for the two-line setup, the
[README](../README.md); for deploying the standalone dashboard service, the
[Install guide](INSTALL.md).

All defaults below are sourced directly from
[`NHibernautOptions.cs`](../src/NHibernaut.Core/NHibernautOptions.cs),
[`NHibernautRuntime.cs`](../src/NHibernaut.Core/NHibernautRuntime.cs), and
[`DashboardHostOptions.cs`](../src/NHibernaut.Server.Host/DashboardHostOptions.cs). Where a default is
computed rather than literal, the cell says so.

You configure `NHibernautOptions` inline through the enable hook — the same options object is consumed
by all three integration tiers:

```csharp
cfg.EnableNHibernaut(o =>                 // Tier A (Core) — NHibernautConfigurationExtensions
{
    o.SlowQueryMs = 100;
    o.CaptureParameterValues = false;     // PII-sensitive production
    o.Dashboard.Port = 6000;
});

builder.Services.AddNHibernaut(o => ...); // Tier C (ASP.NET Core) — same options type
```

`EnableNHibernaut` constructs a fresh `NHibernautOptions`, applies your `configure` delegate, and
publishes it to [`NHibernautRuntime.Options`](../src/NHibernaut.Core/NHibernautRuntime.cs). `AddNHibernaut`
does the same and registers the instance in DI.

---

## 1. All `NHibernautOptions` properties

Type column uses C# notation; `?` marks nullable. Nested `DashboardOptions` members are listed under
their dotted path (`Dashboard.*`). Source: [`NHibernautOptions.cs`](../src/NHibernaut.Core/NHibernautOptions.cs).

### Analysis thresholds

Each gates one detector — see [§2](#2-threshold--detector-map) for the mapping.

| Name | Type | Default | Controls |
|---|---|---|---|
| `NPlusOneThreshold` | `int` | `10` | N+1 trigger: ≥ this many statements sharing a `NormalizedSql`, or ≥ this many collection-inits of one role, in a session. |
| `TooManyQueriesThreshold` | `int` | `50` | Session statement count above this raises `TooManyQueries`. |
| `UnboundedResultSetRowThreshold` | `int` | `100` | An unlimited `SELECT` returning more than this many rows raises `UnboundedResultSet`. |
| `TooManyRowsThreshold` | `int` | `1000` | A single statement returning more than this many rows raises `TooManyRows`. |
| `TooManyJoinsThreshold` | `int` | `5` | `JOIN` count in one statement above this raises `TooManyJoins`. |
| `SlowQueryMs` | `int` | `200` | Statement duration (ms) above this raises `SlowQuery`. |
| `TooManyWritesThreshold` | `int` | `50` | Session write count above this raises `TooManyWrites`. |

### Capture

| Name | Type | Default | Controls |
|---|---|---|---|
| `CaptureParameterValues` | `bool` | `true` | Capture bound parameter values. Disable for PII-sensitive production. |
| `CaptureStackTraces` | `bool` | `IsDevelopmentEnvironment()` — `true` when `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` = `Development`, else `false` | Capture filtered stack traces (expensive). Required for click-to-source. |
| `StackTraceNamespaceFilter` | `string[]` | `{ "NHibernate.", "System.Data.", "NHibernaut." }` | Namespace prefixes whose frames are stripped from captured stack traces. |
| `MaxCapturedRows` | `int` | `10_000` | Upper bound on rows counted per reader (prevents pathological counting on huge results). |

### Redaction / PII

| Name | Type | Default | Controls |
|---|---|---|---|
| `ParameterRedactor` | `Func<ParamContext, object?>?` | `null` | Per-value redaction hook applied to each captured parameter. Receives `ParamContext` (`ParameterName`, `Value`, `DbType`, `Sql`); return the masked value. See [§3](#3-runtime-extension-knobs). |

> `CaptureParameterValues = false` drops values outright; `ParameterRedactor` masks them in place.

### Sampling

| Name | Type | Default | Controls |
|---|---|---|---|
| `SamplingRate` | `double` | `1.0` | Fraction of sessions to profile, `0..1`. `1.0` = all; `0.0` = none. Decision is stable per session id — see [`IsSampled`](#3-runtime-extension-knobs). |

### Retention

| Name | Type | Default | Controls |
|---|---|---|---|
| `RetentionSessionCount` | `int` | `200` | Retain at most this many sessions in the in-memory store (oldest pruned). |
| `RetentionMaxAge` | `TimeSpan` | `TimeSpan.FromMinutes(30)` | Drop sessions older than this. |

### Dashboard (nested `Dashboard.*`)

`Dashboard` is a non-null `DashboardOptions` instance (default `new()`); mutate its members in place.

| Name | Type | Default | Controls |
|---|---|---|---|
| `Dashboard` | `DashboardOptions` | `new DashboardOptions()` | Container for the in-process dashboard server settings below. |
| `Dashboard.AutoStartServer` | `bool` | `false` | Start the in-process dashboard server automatically when capture is enabled. |
| `Dashboard.BindAddress` | `string` | `"127.0.0.1"` | Bind address for the **in-process** server. Loopback by default to avoid exposing SQL/parameter values. |
| `Dashboard.Port` | `int` | `5005` | TCP port for the in-process server. |
| `Dashboard.AuthToken` | `string?` | `null` | Required when `Dashboard.BindAddress` is **not** loopback; the in-process server **refuses to start** without it. When set, enforced on every request (`X-NHibernaut-Token` header or `?token=`). |
| `Dashboard.EnabledInProduction` | `bool` | `false` | Allow the dashboard in Production (Tier C hides it otherwise). Off by default — the dashboard exposes sensitive data. |
| `Dashboard.Authorize` | `Func<object?, bool>?` | `null` | Optional Tier C authorization hook; the argument is the host-specific request context. |
| `Dashboard.EditorLinkScheme` | `string` | `"vscode"` | URI scheme for click-to-source deep links (e.g. `vscode://file/...`). |

> **Two different bind defaults.** `Dashboard.BindAddress` (in-process library) defaults to
> `127.0.0.1`; the **standalone service** `NHIBERNAUT_BIND` defaults to `0.0.0.0` — see [§4](#4-standalone-service-environment-variables).
> The auth-token behavior also differs: the library *refuses to start* on a non-loopback bind without
> a token; the service *auto-generates and logs* one instead.

`ParamContext` (passed to `ParameterRedactor`) is a small read-only record:

| Member | Type | Meaning |
|---|---|---|
| `ParameterName` | `string?` | Bound parameter name. |
| `Value` | `object?` | The captured value to mask. |
| `DbType` | `string?` | ADO.NET DB type of the parameter. |
| `Sql` | `string?` | SQL the parameter belongs to. |

---

## 2. Threshold → detector map

7 of the 11 analysis detectors are gated by a `NHibernautOptions` threshold; the comparison is
**strictly greater than** the option (`>`), except the two N+1 paths which fire at `>=` the option.
Source: [`Detectors.cs`](../src/NHibernaut.Core/Analysis/Detectors.cs).

| Threshold option | Detector | Fires when |
|---|---|---|
| `NPlusOneThreshold` | `SelectNPlusOneDetector` | statements of one shape, or collection-inits of one role, **≥** threshold |
| `TooManyQueriesThreshold` | `TooManyQueriesDetector` | `session.StatementCount` **>** threshold |
| `UnboundedResultSetRowThreshold` | `UnboundedResultSetDetector` | unlimited `SELECT` `RowsRead` **>** threshold |
| `TooManyRowsThreshold` | `TooManyRowsDetector` | statement `RowsRead` **>** threshold |
| `TooManyJoinsThreshold` | `TooManyJoinsDetector` | `JOIN` count in one statement **>** threshold |
| `SlowQueryMs` | `SlowQueryDetector` | statement `DurationMs` **>** threshold |
| `TooManyWritesThreshold` | `TooManyWritesDetector` | `session.WriteCount` **>** threshold |

The remaining **4 detectors have no threshold knob** — they fire on structure, not a tunable count:
`DuplicateQueryDetector` (identical SQL + identical params > once), `CrossThreadSessionDetector` (one
session on > 1 thread), `WriteWithoutTransactionDetector` (a write outside any transaction window),
`SuperfluousUpdateDetector` (an `UPDATE` with no tracked change). The full alert catalogue (types,
severities, suggestions) lives in the [README](../README.md#alert-catalogue).

---

## 3. Runtime extension knobs

[`NHibernautRuntime`](../src/NHibernaut.Core/NHibernautRuntime.cs) is the static singleton all capture
routes through. Most members are set by `EnableNHibernaut`; a few are extension/diagnostic points.

| Member | Type | Setter | Notes |
|---|---|---|---|
| `Options` | `NHibernautOptions` | `internal set` | Active options; published by `EnableNHibernaut` / `AddNHibernaut`. Never null. |
| `Store` | `IProfilerStore` | `internal set`¹ | Active session sink. Default `InMemoryProfilerStore`; replaced by `EnableNHibernaut`. |
| `Analysis` | `AnalysisPipeline` | `internal set`¹ | Pipeline run on session seal. Default = all 11 detectors. |
| `TierAActive` | `bool` | `internal set` | `true` once `EnableNHibernaut` has run; the Tier B logging baseline stands down while set. |
| `InternalError` | `event Action<Exception>?` | `public` (event) | Diagnostic channel for swallowed internal errors. Never throws into the host. |

> ¹ **`Store` and `Analysis` have an `internal` setter.** `InternalsVisibleTo` in
> [`NHibernaut.Core.csproj`](../src/NHibernaut.Core/NHibernaut.Core.csproj) extends only to
> `NHibernaut.Tests` and `NHibernaut.AspNetCore`, so the `NHibernautRuntime.Store = …` /
> `NHibernautRuntime.Analysis = …` snippets shown in [ARCHITECTURE §11](ARCHITECTURE.md#11-extension-points)
> and [CONTRIBUTING](../CONTRIBUTING.md) **do not compile from a third-party consumer assembly** as
> written. They work from within the solution. See the discrepancy note at the bottom.

### Redaction / PII wiring — `ParameterRedactor` (public, the supported hook)

`ParameterRedactor` is a `public set` on `NHibernautOptions`, so it is the sanctioned way to scrub
captured values without forking a store:

```csharp
cfg.EnableNHibernaut(o =>
{
    o.ParameterRedactor = ctx =>
        ctx.ParameterName?.Contains("ssn", StringComparison.OrdinalIgnoreCase) == true
            ? "***"
            : ctx.Value;
});
```

### Store wiring (in-solution only)

```csharp
NHibernautRuntime.Store = new MyOtelStore();   // compiles only from NHibernaut.Tests / .AspNetCore
```

### Analysis pipeline wiring (in-solution only)

```csharp
NHibernautRuntime.Analysis = new AnalysisPipeline(
    AnalysisPipeline.DefaultDetectors().Append(new MyDetector()));
```

### Sampling — `IsSampled`

`SamplingRate` is read through `NHibernautRuntime.IsSampled(sessionId)`, which gates every capture
entry point. The decision is **stable per session id** (derived from the id hash), so every capture
point agrees for the session's lifetime; `>= 1.0` always samples, `<= 0.0` never does:

```csharp
if (!NHibernautRuntime.IsSampled(sessionId)) return;   // non-sampled sessions are never created
```

### Fail-safety — `SafeExecute` / `ReportInternalError`

Internal capture work is wrapped so an error never surfaces to the host; it is routed to the
`InternalError` event instead:

```csharp
NHibernautRuntime.InternalError += ex => _log.Warn(ex, "nhibernaut internal");
NHibernautRuntime.SafeExecute(() => DoCapture());        // void overload swallows + reports
var n = NHibernautRuntime.SafeExecute(() => Count(), 0); // generic overload returns a fallback
```

---

## 4. Standalone service environment variables

The deployable dashboard ([`NHibernaut.Server.Host`](../src/NHibernaut.Server.Host/Program.cs)) is
**not** configured through `NHibernautOptions`. It reads three environment variables, resolved by
[`DashboardHostOptions.Resolve`](../src/NHibernaut.Server.Host/DashboardHostOptions.cs). For installers,
per-platform env-var locations, and log paths, see the [Install guide](INSTALL.md).

| Variable | Default | Validation | Meaning |
|---|---|---|---|
| `NHIBERNAUT_BIND` | `0.0.0.0` | trimmed; blank → default | Bind address. Use `127.0.0.1`/`localhost` to restrict to loopback. |
| `NHIBERNAUT_PORT` | `5005` | integer `1..65535`, else `FormatException` at start | TCP port. |
| `NHIBERNAUT_AUTH_TOKEN` | *(empty)* | see auth-token rule below | Bearer token enforced on every request (`X-NHibernaut-Token` header or `?token=`). |

**Auth-token rule.** When the resolved bind is **non-loopback** (`IsLoopback` checks `localhost` and
any IP that parses as loopback, including bracketed IPv6) **and** `NHIBERNAUT_AUTH_TOKEN` is blank,
the service **generates a random token and logs it** (`TokenWasGenerated = true`), then enforces it on
every request. The token is hex (`Convert.ToHexString` of 32 random bytes) so it survives the
documented `?token=` login (Base64's `+` would decode to a space and be rejected). For a stable token,
set `NHIBERNAUT_AUTH_TOKEN` yourself. On a loopback bind a missing token is allowed (no token enforced).

> This is the inverse of the in-process library, where a non-loopback bind without `Dashboard.AuthToken`
> *refuses to start* rather than auto-generating one.

---

## Defaults that disagree with the README

**None of the documented default values disagree with the README.** All seven analysis thresholds, the
in-process bind/port (`127.0.0.1` / `5005`), the service env-var defaults (`0.0.0.0` / `5005` / empty),
retention (`200` / `30 min`), and `EditorLinkScheme` (`vscode`) match between code and the prose docs.

The one **drift** found is structural, not a default value: the `NHibernautRuntime.Store = …` and
`NHibernautRuntime.Analysis = …` extension-point snippets in [ARCHITECTURE §11](ARCHITECTURE.md#11-extension-points)
and [CONTRIBUTING](../CONTRIBUTING.md) won't compile from a third-party assembly because those setters
are `internal` and `InternalsVisibleTo` covers only the test and ASP.NET Core assemblies — documented
in [§3](#3-runtime-extension-knobs) above.
