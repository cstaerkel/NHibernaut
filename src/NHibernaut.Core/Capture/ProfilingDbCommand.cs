using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Decorates a real <see cref="DbCommand"/> to capture executed SQL. All members delegate to the
/// inner command; the Execute* paths (sync and async) emit a capture before delegating.
/// </summary>
/// <remarks>
/// Walking-skeleton scope: captures the command text only. Milestone 2 enriches this with timing,
/// parameter snapshots, row counts, exception capture and reader-close attribution.
/// </remarks>
public sealed class ProfilingDbCommand : DbCommand
{
    private readonly DbCommand _inner;
    private DbConnection? _connection;

    public ProfilingDbCommand(DbCommand inner, DbConnection? connection)
    {
        _inner = inner;
        _connection = connection;
    }

    /// <summary>The decorated command.</summary>
    public DbCommand Inner => _inner;

    [AllowNull] // base DbCommand.CommandText setter permits null
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value;
    }

    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set
        {
            _connection = value;
            // NHibernate assigns the (profiling) session connection; the inner command must talk to
            // the real underlying connection.
            _inner.Connection = Unwrap(value);
        }
    }

    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    private DbTransaction? _transaction;

    protected override DbTransaction? DbTransaction
    {
        get => _transaction;
        set
        {
            _transaction = value;
            _inner.Transaction = Unwrap(value);
        }
    }

    public override void Cancel() => _inner.Cancel();

    public override void Prepare() => _inner.Prepare();

    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    public override int ExecuteNonQuery()
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var rows = _inner.ExecuteNonQuery();
            Capturer.Complete(capture, rows);
            return rows;
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    public override object? ExecuteScalar()
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var result = _inner.ExecuteScalar();
            Capturer.Complete(capture, null);
            return result;
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var reader = _inner.ExecuteReader(behavior);
            return capture is null ? reader : new ProfilingDbDataReader(reader, capture);
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    public override async Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var rows = await _inner.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Capturer.Complete(capture, rows);
            return rows;
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    public override async Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var result = await _inner.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            Capturer.Complete(capture, null);
            return result;
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken cancellationToken)
    {
        var capture = Capturer.Begin(_inner);
        try
        {
            var reader = await _inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            return capture is null ? reader : new ProfilingDbDataReader(reader, capture);
        }
        catch (Exception ex)
        {
            Capturer.Fail(capture, ex);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    private static DbConnection? Unwrap(DbConnection? connection)
        => connection is ProfilingDbConnection p ? p.Inner : connection;

    private static DbTransaction? Unwrap(DbTransaction? transaction)
        => transaction is ProfilingDbTransaction p ? p.Inner : transaction;
}
