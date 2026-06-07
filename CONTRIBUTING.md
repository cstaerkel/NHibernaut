# Contributing to NHibernaut

Thanks for your interest! NHibernaut is MIT-licensed. This guide covers building, testing, and the
common extension points. For how the pieces fit together, read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Prerequisites

- **.NET 10 SDK** (the version is pinned in `global.json`).
- That's it — tests use an in-memory SQLite database via `Microsoft.Data.Sqlite`, no external services.

## Build & test

```bash
dotnet build
dotnet test          # the whole solution is the gate — keep it green
```

The suite is the source of truth. It mixes:

- **Unit tests** for the pure pieces (SQL normalizer, each `IAlertDetector` against synthetic streams,
  options/redaction, the in-memory store, fail-safety).
- **Integration tests** against a **real `SessionFactory` + real SQL** (SQLite in-memory). We do **not**
  mock NHibernate — the N+1 test triggers genuine lazy loading in a loop, etc. The shared SQLite
  fixture holds one keep-alive connection open so the in-memory DB survives across sessions.
- **Server tests** start `NHibernautServer` on an ephemeral loopback port and hit the JSON/SSE endpoints
  with `HttpClient`; the Tier C test uses an in-memory `TestHost`.

Tests that touch the static `NHibernautRuntime` run **sequentially** (the assembly disables test
parallelization) to avoid cross-test contamination of the shared store.

## Run the samples

```bash
# Console: enables NHibernaut in two lines, self-hosts the dashboard, generates N+1 / writes / txns.
dotnet run --project samples/NHibernaut.Sample.Console   # then open http://localhost:5005

# Web (Tier C): mounts the dashboard at /nhibernaut and adds Server-Timing headers.
dotnet run --project samples/NHibernaut.Sample.Web
```

## Project layout

```
src/NHibernaut.Core/        capture, model, analysis, storage, runtime, logging baseline (no web dep)
src/NHibernaut.Server/      HttpListener dashboard server + embedded SPA (wwwroot/)
src/NHibernaut.AspNetCore/  optional ASP.NET Core (Tier C) integration
test/NHibernaut.Tests/      unit + integration + server + SSE + Tier C tests (Mn_* by milestone)
samples/                    console (self-hosted dashboard) + web (Tier C)
```

## Coding conventions

- C# `latest`, `Nullable enable`, **explicit usings** (implicit usings are off).
- **Capture code must be fail-safe.** Anything on a capture path wraps its body in
  `NHibernautRuntime.SafeExecute(...)` (or is called from one) so an internal error is swallowed and
  routed to `InternalError` — it must never surface to the host. Add a test proving it.
- Keep the public surface minimal: `EnableNHibernaut`, `NHibernautServer.Start/Stop`,
  `AddNHibernaut`/`UseNHibernaut`/`MapNHibernaut`, `NHibernautOptions`, `IProfilerStore`, `IAlertDetector`.
- Follow TDD: write the failing test first, watch it fail for the right reason, then implement.

## Adding an alert detector

1. Implement `IAlertDetector` in `src/NHibernaut.Core/Analysis/` (keep it pure — no side effects):

   ```csharp
   public sealed class MyDetector : IAlertDetector
   {
       public IEnumerable<Alert> Detect(ProfiledSession session, NHibernautOptions options)
       {
           // inspect session.Statements / Writes / etc.; yield Alert objects with a
           // Title, Description, Severity and a concrete Suggestion.
       }
   }
   ```

2. Add a **unit test** against a synthetic `ProfiledSession` (see `M5_DetectorTests`): one case that
   fires above the threshold and one that does not.
3. Register it. For the built-in set, add it to `AnalysisPipeline.DefaultDetectors()`. To add one
   without forking, compose a pipeline at startup:

   ```csharp
   NHibernautRuntime.Analysis = new AnalysisPipeline(
       AnalysisPipeline.DefaultDetectors().Append(new MyDetector()));
   ```

4. If the threshold is configurable, add it to `NHibernautOptions` with a sensible default and assert
   the default in `M1_OptionsTests`.

## Custom storage sink

Implement `IProfilerStore` (e.g. to forward to a file or OTLP) and set
`NHibernautRuntime.Store = new MyStore();` after `EnableNHibernaut`. The default
`InMemoryProfilerStore` shows the expected semantics (bounded retention, `SessionSealed` event).

## Submitting changes

- Branch from `main`, keep commits focused, and ensure `dotnet build` + `dotnet test` are green.
- Describe the behavior change and include a test for it.
