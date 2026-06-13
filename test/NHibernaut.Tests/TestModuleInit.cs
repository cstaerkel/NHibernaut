using System.Runtime.CompilerServices;
using NHibernaut.Core;

namespace NHibernaut.Tests;

internal static class TestModuleInit
{
    // Touch NHibernaut.Core here so its Tier B logging-baseline module initializer runs — installing the
    // capturing NHibernate.SQL logger factory — BEFORE any test builds a SessionFactory.
    //
    // Why this matters: NHibernate's SqlStatementLogger caches the NHibernate.SQL logger in a *static*
    // field the first time any SessionFactory is built in the process. If that first build happens before
    // our factory is installed, the default (disabled) logger is cached for the entire run and Tier B
    // captures nothing. xUnit doesn't guarantee test order, so whether the first SessionFactory built was
    // a plain (Tier B) one or one that had already touched NHibernaut.Core varied run-to-run — making
    // M8_LoggingBaselineTests.Tier_B_captures_sql_without_EnableNHibernaut intermittently fail.
    [ModuleInitializer]
    internal static void Init() => _ = NHibernautRuntime.Options;
}
