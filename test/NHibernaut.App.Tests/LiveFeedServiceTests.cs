using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NHibernaut.App.Services;
using NHibernaut.Client;
using NHibernaut.Core.Model;
using NHibernaut.Server;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class LiveFeedServiceTests
{
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public Task InvokeAsync(Func<Task> action) => action();
    }

    private sealed class StreamingFakeClient : IDashboardClient
    {
        private readonly SessionSummaryDto _session;
        public StreamingFakeClient(SessionSummaryDto session) => _session = session;

        public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

        public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new DashboardConfig("vscode"));

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummaryDto>>(Array.Empty<SessionSummaryDto>());

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
            yield return _session;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// First StreamSessionsAsync call yields one session then COMPLETES (simulating EOF/server restart);
    /// the next call yields a different session. Proves the pump reconnects rather than dying on EOF.
    /// </summary>
    private sealed class ReconnectingFakeClient : IDashboardClient
    {
        private readonly SessionSummaryDto _first;
        private readonly SessionSummaryDto _second;
        private int _calls;

        public ReconnectingFakeClient(SessionSummaryDto first, SessionSummaryDto second)
        { _first = first; _second = second; }

        public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

        public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new DashboardConfig("vscode"));

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummaryDto>>(Array.Empty<SessionSummaryDto>());

        public Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<SessionDetailDto?>(null);

        public Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AggregateRowDto>>(Array.Empty<AggregateRowDto>());

        public Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AlertFeedItemDto>>(Array.Empty<AlertFeedItemDto>());

        public Task ClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            var call = Interlocked.Increment(ref _calls);
            await Task.Yield();
            if (call == 1) { yield return _first; yield break; }   // EOF after one item → forces reconnect
            yield return _second;
            // Keep the second stream open until cancelled so the loop doesn't spin.
            await Task.Delay(System.Threading.Timeout.Infinite, ct).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Start_reconnects_after_stream_completes_and_receives_later_sessions()
    {
        var first = new SessionSummaryDto("sess-1", DateTimeOffset.UtcNow, null, true, 1, 50.0, 0, 0, 0, null, 1);
        var second = new SessionSummaryDto("sess-2", DateTimeOffset.UtcNow, null, true, 1, 50.0, 0, 0, 0, null, 1);
        var client = new ReconnectingFakeClient(first, second);
        var dispatcher = new ImmediateDispatcher();
        // Tiny reconnect delay so the loop reconnects fast.
        var feed = new LiveFeedService(dispatcher, NullLogger<LiveFeedService>.Instance, TimeSpan.FromMilliseconds(10));

        var received = new List<string>();
        var bothSeen = new TaskCompletionSource();
        var gate = new object();
        feed.SessionReceived += s =>
        {
            lock (gate)
            {
                received.Add(s.Id);
                if (received.Contains("sess-1") && received.Contains("sess-2"))
                    bothSeen.TrySetResult();
            }
        };

        feed.Start(client);

        // Both sessions arrive only if the pump reconnected after the first stream's EOF.
        await bothSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (gate)
        {
            Assert.Contains("sess-1", received);
            Assert.Contains("sess-2", received);
        }

        // Stop() must end the loop cleanly: DisposeAsync awaits the pump and returns.
        await feed.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Start_fires_SessionReceived_for_streamed_session()
    {
        var dto = new SessionSummaryDto("sess-1", DateTimeOffset.UtcNow, null, true, 1, 50.0, 0, 0, 0, null, 1);
        var client = new StreamingFakeClient(dto);
        var dispatcher = new ImmediateDispatcher();
        var feed = new LiveFeedService(dispatcher, NullLogger<LiveFeedService>.Instance);

        var tcs = new TaskCompletionSource<SessionSummaryDto>();
        feed.SessionReceived += s => tcs.TrySetResult(s);

        feed.Start(client);

        var received = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("sess-1", received.Id);

        await feed.DisposeAsync();
    }

    /// <summary>Captures posted UI actions without running them, so a test can deliver them on demand.</summary>
    private sealed class QueuingDispatcher : IUiDispatcher
    {
        private readonly object _gate = new();
        private readonly List<Action> _pending = new();
        public void Post(Action action) { lock (_gate) _pending.Add(action); }
        public Task InvokeAsync(Func<Task> action) => action();
        public int PendingCount { get { lock (_gate) return _pending.Count; } }
        public void Flush()
        {
            List<Action> copy;
            lock (_gate) { copy = new List<Action>(_pending); _pending.Clear(); }
            foreach (var a in copy) a();
        }
    }

    /// <summary>Yields one session, then holds the stream open until cancelled (no EOF-driven reconnect).</summary>
    private sealed class OneThenBlockClient : IDashboardClient
    {
        private readonly SessionSummaryDto _session;
        public OneThenBlockClient(SessionSummaryDto session) => _session = session;

        public DashboardConnection Connection => DashboardConnection.Remote("http://localhost");

        public Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(new DashboardConfig("vscode"));

        public Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SessionSummaryDto>>(Array.Empty<SessionSummaryDto>());

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
            yield return _session;
            try { await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Restart_drops_session_callbacks_queued_by_the_previous_pump()
    {
        var oldDto = new SessionSummaryDto("old", DateTimeOffset.UtcNow, null, true, 1, 50.0, 0, 0, 0, null, 1);
        var newDto = new SessionSummaryDto("new", DateTimeOffset.UtcNow, null, true, 1, 50.0, 0, 0, 0, null, 1);
        var dispatcher = new QueuingDispatcher();
        var feed = new LiveFeedService(dispatcher, NullLogger<LiveFeedService>.Instance, TimeSpan.FromMilliseconds(10));

        var received = new List<string>();
        feed.SessionReceived += s => received.Add(s.Id);   // only ever runs on this thread, inside Flush()

        // First pump queues a SessionReceived("old") callback (after a "connected" status post).
        feed.Start(new OneThenBlockClient(oldDto));
        await WaitUntil(() => dispatcher.PendingCount >= 2, TimeSpan.FromSeconds(5));

        // Reconnect: a new pump (new generation) supersedes the first before its callbacks are delivered.
        feed.Start(new OneThenBlockClient(newDto));

        // Deliver everything queued; the stale "old" callback must be fenced out by generation.
        await WaitUntil(() => { dispatcher.Flush(); return received.Contains("new"); }, TimeSpan.FromSeconds(5));

        Assert.Contains("new", received);
        Assert.DoesNotContain("old", received);

        await feed.DisposeAsync();
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition was not met within the timeout");
            await Task.Delay(10);
        }
    }
}
