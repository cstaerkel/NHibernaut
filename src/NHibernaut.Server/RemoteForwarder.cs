using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Core.Storage;

namespace NHibernaut.Server;

/// <summary>
/// Forwards each sealed <see cref="ProfiledSession"/> from the local store to a remote NHibernaut
/// dashboard's <c>POST /api/ingest</c> endpoint, so one centrally-deployed dashboard can show many
/// apps' sessions. Sending is asynchronous, bounded, and fully fail-safe: it never blocks or throws
/// into capture, and if the remote server is unreachable, sessions are dropped rather than queued
/// without bound.
///
/// Enable this in the apps you want to profile — not on the central dashboard host itself, which
/// would re-forward the very sessions it ingests.
/// </summary>
public sealed class RemoteForwarder : IDisposable
{
    private static readonly object Gate = new();
    private static RemoteForwarder? _current;

    private readonly IProfilerStore _store;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _ingestUri;
    private readonly string? _token;
    private readonly Channel<SessionDetailDto> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    /// <summary>
    /// Starts forwarding sealed sessions from <see cref="NHibernautRuntime.Store"/> to the remote
    /// dashboard at <paramref name="serverUrl"/>. Idempotent — repeated calls return the same instance.
    /// </summary>
    /// <param name="serverUrl">Base URL of the remote dashboard, e.g. <c>http://dashboard:5005</c>.</param>
    /// <param name="token">The remote dashboard's auth token, if it requires one.</param>
    public static RemoteForwarder Enable(string serverUrl, string? token = null)
    {
        lock (Gate)
        {
            _current ??= new RemoteForwarder(serverUrl, token, NHibernautRuntime.Store);
            return _current;
        }
    }

    /// <summary>Stops the active forwarder, if any.</summary>
    public static void Disable()
    {
        lock (Gate)
        {
            _current?.Dispose();
            _current = null;
        }
    }

    public RemoteForwarder(string serverUrl, string? token, IProfilerStore store, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            throw new ArgumentException("serverUrl is required", nameof(serverUrl));

        _store = store ?? throw new ArgumentNullException(nameof(store));
        _token = string.IsNullOrEmpty(token) ? null : token;
        var baseUrl = serverUrl.EndsWith("/", StringComparison.Ordinal) ? serverUrl : serverUrl + "/";
        _ingestUri = new Uri(new Uri(baseUrl), "api/ingest");
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
        _queue = Channel.CreateBounded<SessionDetailDto>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite, // never block capture; drop if the server lags
            SingleReader = true
        });

        _store.SessionSealed += OnSealed;
        _pump = Task.Run(PumpAsync);
    }

    private void OnSealed(ProfiledSession session)
    {
        // Snapshot under the session lock now (ToDetail locks SyncRoot); the POST happens off-thread.
        try { _queue.Writer.TryWrite(DtoMapper.ToDetail(session)); }
        catch (Exception ex) { NHibernautRuntime.ReportInternalError(ex); }
    }

    private async Task PumpAsync()
    {
        try
        {
            await foreach (var dto in _queue.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    var json = JsonSerializer.Serialize(dto, NHibernautServer.JsonOptions);
                    using var request = new HttpRequestMessage(HttpMethod.Post, _ingestUri)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    if (_token is not null) request.Headers.TryAddWithoutValidation("X-NHibernaut-Token", _token);
                    using var response = await _http.SendAsync(request, _cts.Token).ConfigureAwait(false);
                    // Best-effort: a non-success status is not retried (forwarding must never back up).
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { NHibernautRuntime.ReportInternalError(ex); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    public void Dispose()
    {
        _store.SessionSealed -= OnSealed;
        _queue.Writer.TryComplete();
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _pump.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _cts.Dispose();
        if (_ownsHttp) _http.Dispose();

        lock (Gate)
        {
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }
}
