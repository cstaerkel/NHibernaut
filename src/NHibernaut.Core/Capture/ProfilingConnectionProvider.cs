using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using NHibernate.Connection;
using NHibernate.Impl;
using NHibernaut.Core.Model;

namespace NHibernaut.Core.Capture;

/// <summary>
/// Connection provider that delegates acquisition/release to the host's originally-configured
/// (inner) provider, but returns connections wrapped in <see cref="ProfilingDbConnection"/>.
/// NHibernate instantiates this via reflection from the <c>connection.provider</c> property; the
/// inner provider type is read from a private property stashed by <c>EnableNHibernaut</c>.
/// </summary>
public class ProfilingConnectionProvider : ConnectionProvider
{
    /// <summary>Property key under which EnableNHibernaut stashes the host's original provider type.</summary>
    internal const string InnerProviderPropertyKey = "nhibernaut.inner.provider";

    private ConnectionProvider _inner = null!;

    public override void Configure(IDictionary<string, string> settings)
    {
        // Configure the base so its (non-overridable) Driver/ConnectionString are populated from
        // the host's settings — NHibernate reads ConnectionProvider.Driver to generate commands.
        base.Configure(settings);

        if (!settings.TryGetValue(InnerProviderPropertyKey, out var innerTypeName) || string.IsNullOrWhiteSpace(innerTypeName))
            innerTypeName = typeof(DriverConnectionProvider).AssemblyQualifiedName!;

        var innerType = Type.GetType(innerTypeName)
            ?? throw new TypeLoadException($"NHibernaut: could not load inner connection provider '{innerTypeName}'.");

        _inner = (ConnectionProvider)Activator.CreateInstance(innerType)!;
        _inner.Configure(settings);
    }

    public override DbConnection GetConnection()
        => Wrap(_inner.GetConnection());

    public override DbConnection GetConnection(string connectionString)
        => Wrap(_inner.GetConnection(connectionString));

    public override async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken)
        => Wrap(await _inner.GetConnectionAsync(cancellationToken).ConfigureAwait(false));

    public override async Task<DbConnection> GetConnectionAsync(string connectionString, CancellationToken cancellationToken)
        => Wrap(await _inner.GetConnectionAsync(connectionString, cancellationToken).ConfigureAwait(false));

    public override void CloseConnection(DbConnection conn)
    {
        if (conn is ProfilingDbConnection p)
        {
            // Record close and seal the owning session. Connection close is NHibernate's only
            // observable "session done" signal (see PROGRESS.md / no session-close hook exists).
            NHibernautRuntime.SafeExecute(() =>
            {
                p.Record.ClosedAt = DateTimeOffset.UtcNow;
                NHibernautRuntime.Store.SealSession(p.SessionId);
            });
            _inner.CloseConnection(p.Inner);
        }
        else
        {
            _inner.CloseConnection(conn);
        }
    }

    private static DbConnection Wrap(DbConnection real)
    {
        var sessionId = SessionIdLoggingContext.SessionId ?? Guid.Empty;
        var record = new ProfiledConnection { OpenedAt = DateTimeOffset.UtcNow };
        if (NHibernautRuntime.IsSampled(sessionId))
        {
            NHibernautRuntime.SafeExecute(() =>
            {
                var session = NHibernautRuntime.Store.GetOrCreateSession(sessionId);
                lock (session.SyncRoot) session.Connections.Add(record);
            });
        }
        return new ProfilingDbConnection(real, sessionId, record);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing) _inner?.Dispose();
        base.Dispose(isDisposing);
    }
}
