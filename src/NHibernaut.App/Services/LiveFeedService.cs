using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NHibernaut.Client;
using NHibernaut.Server;

namespace NHibernaut.App.Services;

/// <summary>Subscribes to a client's live stream and raises SessionReceived on the UI thread.</summary>
public sealed class LiveFeedService : IAsyncDisposable
{
    private readonly IUiDispatcher _ui;
    private readonly ILogger<LiveFeedService> _log;
    private readonly TimeSpan _baseDelay;
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private volatile int _generation;

    public event Action<SessionSummaryDto>? SessionReceived;

    /// <summary>Raised on the UI thread with "connected" / "reconnecting" / "stopped" as the pump's state changes.</summary>
    public event Action<string>? StatusChanged;

    public LiveFeedService(IUiDispatcher ui, ILogger<LiveFeedService> log, TimeSpan? reconnectBaseDelay = null)
    {
        _ui = ui;
        _log = log;
        // Default base backoff; tests can pass a tiny delay to keep reconnect loops fast.
        _baseDelay = reconnectBaseDelay is { } d && d > TimeSpan.Zero ? d : TimeSpan.FromSeconds(2);
    }

    public void Start(IDashboardClient client)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var gen = ++_generation;   // fence: callbacks queued by a prior pump are dropped once this advances
        _pump = Task.Run(() => RunAsync(client, gen, ct), ct);
    }

    /// <summary>
    /// Reconnect loop: run the stream; on EOF or a non-cancellation fault, log it, raise
    /// "reconnecting", back off (capped), and try again. Exits only on cancellation (Stop/Dispose).
    /// </summary>
    private async Task RunAsync(IDashboardClient client, int gen, CancellationToken ct)
    {
        var delay = _baseDelay;
        while (!ct.IsCancellationRequested)
        {
            var delivered = 0;
            var startedAt = DateTime.UtcNow;
            Post(gen, () => StatusChanged?.Invoke("connected"));
            try
            {
                await foreach (var s in client.StreamSessionsAsync(ct).ConfigureAwait(false))
                {
                    var dto = s;
                    delivered++;
                    Post(gen, () => SessionReceived?.Invoke(dto));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break; // Stop()/DisposeAsync() — clean exit, no reconnect.
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Live feed stream faulted; will reconnect.");
            }

            if (ct.IsCancellationRequested) break;

            // A stream that delivered work (or ran a while) is treated as healthy: reset the backoff.
            if (delivered > 0 || DateTime.UtcNow - startedAt > MaxDelay)
                delay = _baseDelay;

            _log.LogInformation("Live feed disconnected; reconnecting in {Delay}.", delay);
            Post(gen, () => StatusChanged?.Invoke("reconnecting"));
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = delay < MaxDelay
                ? TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxDelay.TotalMilliseconds))
                : MaxDelay;
        }

        Post(gen, () => StatusChanged?.Invoke("stopped"));
    }

    private void Post(int gen, Action action)
    {
        // Drop callbacks from a superseded pump: only the current generation may touch the UI.
        try { _ui.Post(() => { if (gen == _generation) action(); }); } catch { /* dispatcher gone / shutting down */ }
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is not null) { cts.Cancel(); cts.Dispose(); }
    }

    public async ValueTask DisposeAsync() { Stop(); if (_pump is not null) { try { await _pump.ConfigureAwait(false); } catch { } } }
}
