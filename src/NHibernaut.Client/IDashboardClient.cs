using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.Client;

/// <summary>
/// Async gateway to NHibernaut profiling data. Implemented over HTTP (remote) or in-process
/// (embedded). All methods return the public wire DTOs from NHibernaut.Server.
/// </summary>
public interface IDashboardClient : IAsyncDisposable
{
    DashboardConnection Connection { get; }

    Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default);
    Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>Live feed of newly-sealed session summaries (SSE remotely, SessionSealed embedded).</summary>
    IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync(CancellationToken ct = default);
}
