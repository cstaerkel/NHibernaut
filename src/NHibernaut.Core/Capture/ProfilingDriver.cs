using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using NHibernate.Driver;
using NHibernate.Engine;
using NHibernate.SqlCommand;
using NHibernate.SqlTypes;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Wraps the host's real <see cref="IDriver"/>, delegating every member to it except
/// <see cref="GenerateCommand"/>, which returns a <see cref="ProfilingDbCommand"/> wrapping the
/// real command. This is the actual SQL-interception point: NHibernate generates commands via
/// <c>ConnectionProvider.Driver.GenerateCommand</c> (not <c>connection.CreateCommand()</c>), so the
/// driver — not just the connection — must be wrapped for capture to fire.
/// </summary>
/// <remarks>
/// NHibernate instantiates this via reflection from <c>connection.driver_class</c>; the real driver
/// type is read from a private property stashed by <c>EnableNHibernaut</c>, mirroring
/// <see cref="ProfilingConnectionProvider"/>.
/// </remarks>
public sealed class ProfilingDriver : IDriver
{
    /// <summary>Property key under which EnableNHibernaut stashes the host's original driver type.</summary>
    internal const string InnerDriverPropertyKey = "nhibernaut.inner.driver";

    private IDriver _inner = null!;

    public void Configure(IDictionary<string, string> settings)
    {
        if (!settings.TryGetValue(InnerDriverPropertyKey, out var innerTypeName) || string.IsNullOrWhiteSpace(innerTypeName))
            throw new InvalidOperationException(
                "NHibernaut: no inner driver type was stashed. EnableNHibernaut must set " +
                $"'{InnerDriverPropertyKey}' before NHibernate configures the driver.");

        var innerType = Type.GetType(innerTypeName)
            ?? throw new TypeLoadException($"NHibernaut: could not load inner driver '{innerTypeName}'.");

        _inner = (IDriver)Activator.CreateInstance(innerType)!;
        _inner.Configure(settings);
    }

    // The one interception point: wrap the generated command.
    public DbCommand GenerateCommand(CommandType type, SqlString sqlString, SqlType[] parameterTypes)
        => new ProfilingDbCommand(_inner.GenerateCommand(type, sqlString, parameterTypes), null);

    // Everything else delegates to the real driver, unwrapping commands first so the real driver
    // never sees a ProfilingDbCommand it might try to downcast.
    public DbConnection CreateConnection() => _inner.CreateConnection();

    public void PrepareCommand(DbCommand command) => _inner.PrepareCommand(Unwrap(command));

    public DbParameter GenerateParameter(DbCommand command, string name, SqlType sqlType)
        => _inner.GenerateParameter(Unwrap(command), name, sqlType);

    public void RemoveUnusedCommandParameters(DbCommand cmd, SqlString sqlString)
        => _inner.RemoveUnusedCommandParameters(Unwrap(cmd), sqlString);

    public void ExpandQueryParameters(DbCommand cmd, SqlString sqlString, SqlType[] parameterTypes)
        => _inner.ExpandQueryParameters(Unwrap(cmd), sqlString, parameterTypes);

    public void AdjustCommand(DbCommand command) => _inner.AdjustCommand(Unwrap(command));

    public IResultSetsCommand GetResultSetsCommand(ISessionImplementor session)
        => _inner.GetResultSetsCommand(session);

    public DbBatch CreateBatch() => _inner.CreateBatch();

    public void PrepareBatch(DbBatch dbBatch) => _inner.PrepareBatch(dbBatch);

    public DbBatchCommand CreateDbBatchCommandFromDbCommand(DbBatch dbBatch, DbCommand dbCommand)
        => _inner.CreateDbBatchCommandFromDbCommand(dbBatch, Unwrap(dbCommand));

    public bool SupportsMultipleOpenReaders => _inner.SupportsMultipleOpenReaders;
    public bool SupportsMultipleQueries => _inner.SupportsMultipleQueries;
    public bool RequiresTimeSpanForTime => _inner.RequiresTimeSpanForTime;
    public bool SupportsSystemTransactions => _inner.SupportsSystemTransactions;
    public bool SupportsNullEnlistment => _inner.SupportsNullEnlistment;
    public bool SupportsEnlistmentWhenAutoEnlistmentIsDisabled => _inner.SupportsEnlistmentWhenAutoEnlistmentIsDisabled;
    public bool HasDelayedDistributedTransactionCompletion => _inner.HasDelayedDistributedTransactionCompletion;
    public DateTime MinDate => _inner.MinDate;
    public bool CanCreateBatch => _inner.CanCreateBatch;

    private static DbCommand Unwrap(DbCommand command)
        => command is ProfilingDbCommand p ? p.Inner : command;
}
