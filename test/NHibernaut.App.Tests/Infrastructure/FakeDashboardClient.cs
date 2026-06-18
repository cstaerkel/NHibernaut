using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.App.Tests.Infrastructure;

/// <summary>
/// Configurable fake IDashboardClient for unit tests.
/// </summary>
public sealed class FakeDashboardClient : IDashboardClient
{
    private readonly IReadOnlyList<SessionSummaryDto> _sessions;
    private readonly Dictionary<Guid, SessionDetailDto> _details;
    private readonly IReadOnlyList<AggregateRowDto> _aggregate;

    /// <summary>
    /// Optional gate: when set, <see cref="GetSessionDetailAsync"/> awaits this task before
    /// returning, letting tests hold a detail fetch in flight to exercise concurrency.
    /// </summary>
    public Task? DetailGate { get; set; }

    public FakeDashboardClient(
        IReadOnlyList<SessionSummaryDto>? sessions = null,
        Dictionary<Guid, SessionDetailDto>? details = null,
        IReadOnlyList<AggregateRowDto>? aggregate = null)
    {
        _sessions = sessions ?? Array.Empty<SessionSummaryDto>();
        _details = details ?? new Dictionary<Guid, SessionDetailDto>();
        _aggregate = aggregate ?? Array.Empty<AggregateRowDto>();
    }

    public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

    public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
        Task.FromResult(new DashboardConfig("vscode"));

    public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
        Task.FromResult(_sessions);

    public async Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default)
    {
        if (DetailGate is not null) await DetailGate;
        return _details.TryGetValue(id, out var detail) ? detail : null;
    }

    public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default) =>
        Task.FromResult(_aggregate);

    public Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AlertFeedItemDto>>(Array.Empty<AlertFeedItemDto>());

    public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        yield break;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
