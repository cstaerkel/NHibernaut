using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NHibernaut.App.Services;
using NHibernaut.App.Tests.Infrastructure;
using NHibernaut.App.ViewModels;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class MainWindowViewModelTests
{
    private static SessionSummaryDto MakeSummary(string id, int statementCount = 1, int alertCount = 0, string? maxSeverity = null) =>
        new(id, DateTimeOffset.UtcNow, null, true, statementCount, 100.0, 0, 0, alertCount, maxSeverity, 1);

    private static AggregateRowDto MakeAggRow(string sql) =>
        new(sql, 1, 10.0, 10.0, 0, 1, 0);

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();
        public Task LoadAsync() => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
    }

    /// <summary>No-op theme service: avoids touching Avalonia's Application in headless unit tests.</summary>
    private sealed class NoopThemeService : IThemeService
    {
        public void Apply(AppTheme theme) { }
    }

    /// <summary>Records the last theme applied so tests can assert the apply path ran.</summary>
    private sealed class RecordingThemeService : IThemeService
    {
        public AppTheme? Applied { get; private set; }
        public void Apply(AppTheme theme) => Applied = theme;
    }

    /// <summary>Factory that hands back a single, fixed client instance (so the test can inspect it).</summary>
    private sealed class FixedFactory : IDashboardClientFactory
    {
        private readonly IDashboardClient _client;
        public FixedFactory(IDashboardClient client) => _client = client;
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default) =>
            Task.FromResult(_client);
    }

    /// <summary>Records DisposeAsync; optionally throws from Sessions/Aggregate/Compare loads.</summary>
    private sealed class RecordingClient : IDashboardClient
    {
        private readonly IReadOnlyList<SessionSummaryDto> _sessions;
        private readonly IReadOnlyList<AggregateRowDto> _aggregate;
        private readonly bool _throwOnSessions;

        public int DisposeCount { get; private set; }
        public int ClearCount { get; private set; }

        public RecordingClient(
            IReadOnlyList<SessionSummaryDto>? sessions = null,
            IReadOnlyList<AggregateRowDto>? aggregate = null,
            bool throwOnSessions = false)
        {
            _sessions = sessions ?? Array.Empty<SessionSummaryDto>();
            _aggregate = aggregate ?? Array.Empty<AggregateRowDto>();
            _throwOnSessions = throwOnSessions;
        }

        public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

        public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new DashboardConfig("vscode"));

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
            _throwOnSessions
                ? Task.FromException<IReadOnlyList<SessionSummaryDto>>(new InvalidOperationException("sessions load failed"))
                : Task.FromResult(_sessions);

        public Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SessionDetailDto?>(null);

        public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default) =>
            Task.FromResult(_aggregate);

        public Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AlertFeedItemDto>>(Array.Empty<AlertFeedItemDto>());

        public Task ClearAsync(CancellationToken ct = default) { ClearCount++; return Task.CompletedTask; }

        public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            // Hold the stream open until cancelled so the live-feed loop doesn't spin during the test.
            try { await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            yield break;
        }

        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }

    private static MainWindowViewModel BuildVm(IDashboardClient client, out LiveFeedService feed)
    {
        feed = new LiveFeedService(new ImmediateDispatcher(), NullLogger<LiveFeedService>.Instance, TimeSpan.FromMilliseconds(10));
        var settings = new FakeSettingsService();
        var connection = new ConnectionViewModel(new FixedFactory(client));
        return new MainWindowViewModel(
            feed,
            settings,
            new SessionsViewModel(),
            new EditorSchemeProvider(),
            new AggregateViewModel(),
            new CompareViewModel(),
            connection,
            new NoopThemeService());
    }

    [Fact]
    public async Task ToggleTheme_flips_applies_and_persists()
    {
        var feed = new LiveFeedService(new ImmediateDispatcher(), NullLogger<LiveFeedService>.Instance, TimeSpan.FromMilliseconds(10));
        var settings = new FakeSettingsService();
        var theme = new RecordingThemeService();
        var connection = new ConnectionViewModel(new FixedFactory(new RecordingClient()));
        var vm = new MainWindowViewModel(
            feed, settings, new SessionsViewModel(), new EditorSchemeProvider(),
            new AggregateViewModel(), new CompareViewModel(), connection, theme);

        Assert.False(vm.IsLightTheme);

        await vm.ToggleThemeCommand.ExecuteAsync(null);
        Assert.True(vm.IsLightTheme);
        Assert.Equal(AppTheme.Light, theme.Applied);
        Assert.Equal(AppTheme.Light, settings.Current.Theme);

        await vm.ToggleThemeCommand.ExecuteAsync(null);
        Assert.False(vm.IsLightTheme);
        Assert.Equal(AppTheme.Dark, theme.Applied);
        Assert.Equal(AppTheme.Dark, settings.Current.Theme);

        await feed.DisposeAsync();
    }

    [Fact]
    public async Task ClearCommand_clears_sessions_aggregate_and_compare()
    {
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        var sessions = new List<SessionSummaryDto> { MakeSummary(idA, alertCount: 1, maxSeverity: "Error"), MakeSummary(idB) };
        var client = new RecordingClient(sessions: sessions, aggregate: new List<AggregateRowDto> { MakeAggRow("SELECT 1") });
        var vm = BuildVm(client, out var feed);

        // Trigger OnConnectedAsync through the real ConnectionViewModel.
        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://localhost"));
        Assert.Equal("connected", vm.Connection.Status);

        // Populate Compare selections so we can assert they are reset.
        vm.Compare.SelectedA = vm.Compare.Options[0];
        vm.Compare.SelectedB = vm.Compare.Options[1];

        Assert.NotEmpty(vm.Sessions.Sessions);
        Assert.NotEmpty(vm.Aggregate.Rows);
        Assert.NotEmpty(vm.Compare.Options);

        await vm.ClearCommand.ExecuteAsync(null);

        Assert.Empty(vm.Sessions.Sessions);
        Assert.Empty(vm.Aggregate.Rows);
        Assert.Empty(vm.Compare.Options);
        Assert.Empty(vm.Compare.Rows);
        Assert.Null(vm.Compare.SelectedA);
        Assert.Null(vm.Compare.SelectedB);

        await feed.DisposeAsync();
    }

    [Fact]
    public async Task Connect_whose_Sessions_load_throws_disposes_new_client_and_nulls_it()
    {
        var client = new RecordingClient(throwOnSessions: true);
        var vm = BuildVm(client, out var feed);

        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://localhost"));

        // The failed load must dispose the just-created client...
        Assert.True(client.DisposeCount >= 1);
        // ...and ConnectionViewModel must surface the error (because OnConnectedAsync rethrew).
        Assert.StartsWith("error:", vm.Connection.Status);

        // The failed client must have been nulled (not left as a stale disposed reference): a subsequent
        // ClearCommand must NOT call ClearAsync on it, and must not throw.
        await vm.ClearCommand.ExecuteAsync(null);
        Assert.Equal(0, client.ClearCount);
        Assert.Empty(vm.Sessions.Sessions);

        await feed.DisposeAsync();
    }

    [Fact]
    public async Task ShowAggregate_navigates_and_refreshes_rows()
    {
        var client = new RecordingClient(aggregate: new List<AggregateRowDto> { MakeAggRow("SELECT a") });
        var vm = BuildVm(client, out var feed);
        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://localhost"));

        // Clear rows locally, then navigate — refresh-on-navigate should repopulate them.
        vm.Aggregate.Rows.Clear();
        vm.ShowAggregateCommand.Execute(null);

        Assert.Same(vm.Aggregate, vm.CurrentPage);
        // Fire-and-forget refresh; give the continuation a chance to run.
        for (var i = 0; i < 50 && vm.Aggregate.Rows.Count == 0; i++) await Task.Delay(10);
        Assert.NotEmpty(vm.Aggregate.Rows);

        await feed.DisposeAsync();
    }

    [Fact]
    public async Task ShowCompare_navigates_and_preserves_selection_on_refresh()
    {
        var idA = Guid.NewGuid().ToString();
        var idB = Guid.NewGuid().ToString();
        var sessions = new List<SessionSummaryDto> { MakeSummary(idA), MakeSummary(idB) };
        var client = new RecordingClient(sessions: sessions);
        var vm = BuildVm(client, out var feed);
        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://localhost"));

        vm.Compare.SelectedA = vm.Compare.Options[0];
        var selectedAId = vm.Compare.SelectedA!.Id;

        vm.ShowCompareCommand.Execute(null);
        Assert.Same(vm.Compare, vm.CurrentPage);

        // Allow the fire-and-forget RefreshOptionsAsync to complete.
        for (var i = 0; i < 50 && vm.Compare.SelectedA is null; i++) await Task.Delay(10);
        Assert.NotNull(vm.Compare.SelectedA);
        Assert.Equal(selectedAId, vm.Compare.SelectedA!.Id);

        await feed.DisposeAsync();
    }

    /// <summary>Returns a different client per connect attempt; a step may throw to simulate a failed connect.</summary>
    private sealed class SequencedFactory : IDashboardClientFactory
    {
        private readonly Queue<Func<IDashboardClient>> _steps;
        public SequencedFactory(params Func<IDashboardClient>[] steps) => _steps = new Queue<Func<IDashboardClient>>(steps);
        public Task<IDashboardClient> CreateAsync(DashboardConnection c, CancellationToken ct = default)
        {
            var step = _steps.Dequeue();
            try { return Task.FromResult(step()); }
            catch (Exception ex) { return Task.FromException<IDashboardClient>(ex); }
        }
    }

    private static MainWindowViewModel BuildVmWithFactory(IDashboardClientFactory factory, out LiveFeedService feed)
    {
        feed = new LiveFeedService(new ImmediateDispatcher(), NullLogger<LiveFeedService>.Instance, TimeSpan.FromMilliseconds(10));
        var connection = new ConnectionViewModel(factory);
        return new MainWindowViewModel(
            feed,
            new FakeSettingsService(),
            new SessionsViewModel(),
            new EditorSchemeProvider(),
            new AggregateViewModel(),
            new CompareViewModel(),
            connection,
            new NoopThemeService());
    }

    [Fact]
    public async Task Failed_reconnect_resets_label_and_clears_stale_pages()
    {
        var sessions = new List<SessionSummaryDto> { MakeSummary(Guid.NewGuid().ToString(), alertCount: 1, maxSeverity: "Error") };
        var good = new RecordingClient(sessions: sessions, aggregate: new List<AggregateRowDto> { MakeAggRow("SELECT 1") });
        var factory = new SequencedFactory(
            () => good,
            () => throw new InvalidOperationException("connect refused"));
        var vm = BuildVmWithFactory(factory, out var feed);

        // First connection succeeds: label and pages are populated.
        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://good:5005"));
        Assert.NotEqual("disconnected", vm.ConnectionLabel);
        Assert.NotEmpty(vm.Sessions.Sessions);
        Assert.NotEmpty(vm.Aggregate.Rows);

        // Reconnect fails AFTER the previous client was torn down.
        await vm.Connection.ConnectAsync(DashboardConnection.Remote("http://bad:5005"));
        Assert.StartsWith("error:", vm.Connection.Status);

        // The dead connection must not keep showing the old target or its data.
        Assert.Equal("disconnected", vm.ConnectionLabel);
        Assert.Empty(vm.Sessions.Sessions);
        Assert.Empty(vm.Aggregate.Rows);

        await feed.DisposeAsync();
    }
}
