using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.App.ViewModels;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class SessionsViewModelTests
{
    // SessionSummaryDto: Id, StartedAt, EndedAt, IsSealed, StatementCount, TotalDurationMs,
    //                    TotalRowsRead, WriteCount, AlertCount, MaxSeverity, ThreadCount
    private static SessionSummaryDto MakeDto(string id, string? maxSeverity, DateTimeOffset startedAt, int statementCount = 1, int alertCount = 0) =>
        new(id, startedAt, null, true, statementCount, 100.0, 0, 0, alertCount, maxSeverity, 1);

    private sealed class FakeClient : IDashboardClient
    {
        private readonly IReadOnlyList<SessionSummaryDto> _sessions;
        public FakeClient(IReadOnlyList<SessionSummaryDto> sessions) => _sessions = sessions;

        public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
            Task.FromResult(_sessions);

        public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new DashboardConfig("vscode"));

        public Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SessionDetailDto?>(null);

        public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AggregateRowDto>>(Array.Empty<AggregateRowDto>());

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

    [Fact]
    public async Task LoadAsync_sorts_most_severe_first_then_newest()
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<SessionSummaryDto>
        {
            MakeDto("a1", "Info", now),
            MakeDto("b2", "Error", now.AddSeconds(-10)),
            MakeDto("c3", null, now.AddSeconds(5)),
        };
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(sessions));

        // Error has highest SeverityRank (3) so should be first
        Assert.Equal("b2", vm.Sessions[0].Dto.Id);
    }

    [Fact]
    public async Task ApplyLiveSession_upserts_by_id_and_resorts()
    {
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto>()));

        var now = DateTimeOffset.UtcNow;
        vm.ApplyLiveSession(MakeDto("x", "Info", now));
        vm.ApplyLiveSession(MakeDto("x", "Error", now));

        Assert.Single(vm.Sessions);
        Assert.Equal("Error", vm.Sessions[0].Severity);
    }

    [Fact]
    public async Task ApplyLiveSession_ignored_when_paused()
    {
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto>()));
        vm.Live = false;

        vm.ApplyLiveSession(MakeDto("y", "Info", DateTimeOffset.UtcNow));

        Assert.Empty(vm.Sessions);
    }

    [Fact]
    public async Task ApplyLiveSession_caps_collection_at_MaxSessions()
    {
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto>()));

        var now = DateTimeOffset.UtcNow;
        // Insert more than the cap of distinct sessions; the list must not grow past MaxSessions.
        for (var i = 0; i < SessionsViewModel.MaxSessions + 50; i++)
            vm.ApplyLiveSession(MakeDto($"s{i}", null, now.AddSeconds(-i)));

        Assert.Equal(SessionsViewModel.MaxSessions, vm.Sessions.Count);
    }

    [Fact]
    public async Task LoadAsync_caps_collection_at_MaxSessions()
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<SessionSummaryDto>();
        for (var i = 0; i < SessionsViewModel.MaxSessions + 25; i++)
            sessions.Add(MakeDto($"l{i}", null, now.AddSeconds(-i)));

        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(sessions));

        Assert.Equal(SessionsViewModel.MaxSessions, vm.Sessions.Count);
    }

    [Fact]
    public async Task ClearLocal_clears_collection_without_calling_client()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto> { MakeDto("a", "Info", now) }));
        Assert.Single(vm.Sessions);

        vm.ClearLocal();

        Assert.Empty(vm.Sessions);
        Assert.Equal(0, vm.TotalQueries);
        Assert.Equal(0, vm.AlertedSessions);
    }

    [Fact]
    public async Task Recount_is_correct_after_load()
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<SessionSummaryDto>
        {
            MakeDto("s1", "Error", now, statementCount: 3, alertCount: 1),
            MakeDto("s2", null, now.AddSeconds(-1), statementCount: 5, alertCount: 0),
        };
        var vm = new SessionsViewModel();
        await vm.LoadAsync(new FakeClient(sessions));

        Assert.Equal(8, vm.TotalQueries);
        Assert.Equal(1, vm.AlertedSessions);
    }

    [Fact]
    public async Task Setting_Selected_with_factory_populates_Detail_and_clearing_nulls_it()
    {
        // Use valid Guid strings for session ids (Guid.Parse is called in SessionItemViewModel.Id)
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<SessionSummaryDto>
        {
            MakeDto(sessionId, "Info", now),
        };

        var detailCreated = false;
        SessionDetailViewModel? createdDetail = null;
        SessionDetailViewModel Factory()
        {
            detailCreated = true;
            createdDetail = new SessionDetailViewModel();
            return createdDetail;
        }

        var vm = new SessionsViewModel(Factory);
        await vm.LoadAsync(new FakeClient(sessions));

        // Before selection: Detail is null, factory not called
        Assert.Null(vm.Detail);
        Assert.False(detailCreated);

        // Select a session → Detail is set synchronously (factory() + Detail = vm happens before LoadAsync)
        vm.Selected = vm.Sessions[0];

        Assert.True(detailCreated);
        Assert.NotNull(vm.Detail);
        Assert.Same(createdDetail, vm.Detail);

        // Clearing selection → Detail is null
        vm.Selected = null;
        Assert.Null(vm.Detail);
    }

    [Fact]
    public async Task LoadAsync_clears_previous_selection_and_detail()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = new SessionsViewModel(() => new SessionDetailViewModel());

        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto> { MakeDto(Guid.NewGuid().ToString(), "Info", now) }));
        vm.Selected = vm.Sessions[0];
        Assert.NotNull(vm.Selected);
        Assert.NotNull(vm.Detail);

        // Reloading (e.g. reconnecting to a different dashboard) must drop the stale selection/detail.
        await vm.LoadAsync(new FakeClient(new List<SessionSummaryDto> { MakeDto(Guid.NewGuid().ToString(), null, now) }));

        Assert.Null(vm.Selected);
        Assert.Null(vm.Detail);
    }
}
