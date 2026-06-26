using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.Mcp.Tests.Infrastructure;

public sealed class FakeDashboardClient : IDashboardClient
{
    public DashboardConnection Connection { get; } = DashboardConnection.Remote("http://127.0.0.1:5005", "fake-token");

    public List<SessionSummaryDto> Sessions { get; } = new();
    public Dictionary<Guid, SessionDetailDto> Details { get; } = new();
    public List<AggregateRowDto> AggregateRows { get; } = new();
    public List<AlertFeedItemDto> AlertFeed { get; } = new();

    public int? LastSessionsTake { get; private set; }
    public int? LastAlertsTake { get; private set; }
    public int SessionDetailCallCount { get; private set; }
    public int AlertsCallCount { get; private set; }
    public int AggregateCallCount { get; private set; }

    public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default)
        => Task.FromResult(new DashboardConfig("vscode"));

    public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(
        int take = 500,
        DateTimeOffset? since = null,
        AlertSeverity? minSeverity = null,
        CancellationToken ct = default)
    {
        LastSessionsTake = take;
        var result = Sessions
            .Where(s => since is null || s.StartedAt >= since)
            .Where(s => minSeverity is null || Severity(s.MaxSeverity) >= minSeverity)
            .Take(take)
            .ToList();
        return Task.FromResult<IReadOnlyList<SessionSummaryDto>>(result);
    }

    public Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default)
    {
        SessionDetailCallCount++;
        Details.TryGetValue(id, out var detail);
        return Task.FromResult(detail);
    }

    public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default)
    {
        AggregateCallCount++;
        return Task.FromResult<IReadOnlyList<AggregateRowDto>>(AggregateRows.ToList());
    }

    public Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default)
    {
        AlertsCallCount++;
        LastAlertsTake = take;
        return Task.FromResult<IReadOnlyList<AlertFeedItemDto>>(AlertFeed.Take(take).ToList());
    }

    public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

    public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var session in Sessions)
        {
            ct.ThrowIfCancellationRequested();
            yield return session;
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static AlertSeverity Severity(string? value)
        => Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var severity) ? severity : AlertSeverity.Info;
}
