using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using NHibernate.Impl;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Decorates a real <see cref="DbTransaction"/> to record a <see cref="ProfiledTransaction"/> and its
/// eventual outcome (commit / rollback). A transaction disposed without an explicit commit is treated
/// as a rollback (ADO.NET semantics).
/// </summary>
public sealed class ProfilingDbTransaction : DbTransaction
{
    private readonly DbTransaction _inner;
    private readonly DbConnection _connection;
    private readonly ProfiledTransaction _record;

    internal ProfilingDbTransaction(DbTransaction inner, DbConnection connection)
    {
        _inner = inner;
        _connection = connection;
        _record = new ProfiledTransaction { BeganAt = DateTimeOffset.UtcNow };

        NHibernautRuntime.SafeExecute(() =>
        {
            var sessionId = SessionIdLoggingContext.SessionId ?? Guid.Empty;
            if (!NHibernautRuntime.IsSampled(sessionId)) return;
            var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
            lock (session.SyncRoot) session.Transactions.Add(_record);
        });
    }

    /// <summary>The decorated transaction.</summary>
    public DbTransaction Inner => _inner;

    public override IsolationLevel IsolationLevel => _inner.IsolationLevel;

    protected override DbConnection DbConnection => _connection;

    public override void Commit()
    {
        _inner.Commit();
        Complete(TransactionOutcome.Commit);
    }

    public override void Rollback()
    {
        _inner.Rollback();
        Complete(TransactionOutcome.Rollback);
    }

    public override async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _inner.CommitAsync(cancellationToken).ConfigureAwait(false);
        Complete(TransactionOutcome.Commit);
    }

    public override async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        await _inner.RollbackAsync(cancellationToken).ConfigureAwait(false);
        Complete(TransactionOutcome.Rollback);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            Complete(TransactionOutcome.Rollback); // no-op if already completed
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        Complete(TransactionOutcome.Rollback);
    }

    private void Complete(TransactionOutcome outcome)
    {
        NHibernautRuntime.SafeExecute(() =>
        {
            if (_record.CompletedAt is not null) return; // first completion wins
            _record.Outcome = outcome;
            _record.CompletedAt = DateTimeOffset.UtcNow;
        });
    }
}
