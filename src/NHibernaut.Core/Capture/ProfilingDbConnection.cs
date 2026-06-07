using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Decorates a real <see cref="DbConnection"/>. Delegates everything to the inner connection and
/// produces <see cref="ProfilingDbCommand"/> instances from <see cref="CreateDbCommand"/>.
/// </summary>
/// <remarks>
/// Walking-skeleton scope: command creation + delegation. Milestone 2 adds open/close timestamps,
/// release-mode context and transaction wrapping.
/// </remarks>
public sealed class ProfilingDbConnection : DbConnection
{
    private readonly DbConnection _inner;

    public ProfilingDbConnection(DbConnection inner, Guid sessionId, ProfiledConnection record)
    {
        _inner = inner;
        SessionId = sessionId;
        Record = record;
    }

    /// <summary>The decorated connection.</summary>
    public DbConnection Inner => _inner;

    /// <summary>The session this connection was acquired under (ambient at acquisition).</summary>
    public Guid SessionId { get; }

    /// <summary>The model record for this connection (open/close timestamps, statement ids).</summary>
    public ProfiledConnection Record { get; }

#pragma warning disable CS8765 // ConnectionString setter nullability vs base
    public override string ConnectionString
    {
        get => _inner.ConnectionString;
        set => _inner.ConnectionString = value;
    }
#pragma warning restore CS8765

    public override string Database => _inner.Database;

    public override string DataSource => _inner.DataSource;

    public override string ServerVersion => _inner.ServerVersion;

    public override ConnectionState State => _inner.State;

    public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);

    public override void Open() => _inner.Open();

    public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);

    public override void Close() => _inner.Close();

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => new ProfilingDbTransaction(_inner.BeginTransaction(isolationLevel), this);

    protected override async ValueTask<DbTransaction> BeginDbTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        var inner = await _inner.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        return new ProfilingDbTransaction(inner, this);
    }

    protected override DbCommand CreateDbCommand()
        => new ProfilingDbCommand(_inner.CreateCommand(), this);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
