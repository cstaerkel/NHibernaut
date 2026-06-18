using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.Client;

/// <summary>
/// Embedded collector: hosts NHibernautServer in-process (so profiled apps can forward sessions to
/// this desktop) and reads the live store directly via the public DashboardApi — no HTTP round-trip
/// to itself. The live feed bridges IProfilerStore.SessionSealed onto a bounded channel.
/// </summary>
public sealed class InProcessDashboardClient : IDashboardClient
{
    private readonly List<Channel<SessionSummaryDto>> _subscribers = new();
    private readonly object _gate = new();
    private NHibernautOptions _options = new();
    private bool _started;

    public DashboardConnection Connection { get; }

    public InProcessDashboardClient(DashboardConnection connection) =>
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));

    /// <summary>Starts the embedded receiver (idempotent) and subscribes to seal events.</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_started) return Task.CompletedTask;
        _options = new NHibernautOptions
        {
            Dashboard = { BindAddress = Connection.BindAddress, Port = Connection.Port, AuthToken = Connection.Token }
        };
        NHibernautServer.Start(_options);                  // serves /api/ingest + SPA; receives forwards
        NHibernautRuntime.Store.SessionSealed += OnSealed; // public getter + public event
        _started = true;
        return Task.CompletedTask;
    }

    private void OnSealed(ProfiledSession session)
    {
        SessionSummaryDto summary;
        try { summary = DashboardApi.Summarize(session); } catch { return; }
        lock (_gate)
            foreach (var ch in _subscribers) ch.Writer.TryWrite(summary);
    }

    public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(new DashboardConfig(_options.Dashboard.EditorLinkScheme));

    public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
        Task.FromResult(DashboardApi.Sessions(take, since, minSeverity));

    public Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(DashboardApi.SessionDetail(id));

    public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default) =>
        Task.FromResult(DashboardApi.Aggregate(_options));

    public Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default) =>
        Task.FromResult(DashboardApi.Alerts(take));

    public Task ClearAsync(CancellationToken ct = default) { DashboardApi.Clear(); return Task.CompletedTask; }

    public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var ch = Channel.CreateBounded<SessionSummaryDto>(new BoundedChannelOptions(1024) { FullMode = BoundedChannelFullMode.DropOldest });
        lock (_gate) _subscribers.Add(ch);
        try
        {
            await foreach (var s in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return s;
        }
        finally { lock (_gate) _subscribers.Remove(ch); }
    }

    public ValueTask DisposeAsync()
    {
        if (_started)
        {
            NHibernautRuntime.Store.SessionSealed -= OnSealed;
            NHibernautServer.Stop();   // embedded mode OWNS the server lifecycle — must stop it
            _started = false;          // else Start() is idempotent and returns the stale server/port
        }
        lock (_gate) { foreach (var ch in _subscribers) ch.Writer.TryComplete(); _subscribers.Clear(); }
        return ValueTask.CompletedTask;
    }
}
