using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core;
using NHibernaut.Core.Model;

namespace NHibernaut.Server;

/// <summary>
/// Self-hosted, in-process dashboard server built on <see cref="HttpListener"/> (not Kestrel) so it
/// runs in any .NET host. Serves the JSON API, an SSE live feed, and the embedded SPA. Lifecycle is
/// independent of the host's web stack. <see cref="Start"/> is idempotent.
/// </summary>
public sealed class NHibernautServer : IDisposable
{
    private static readonly object Gate = new();
    private static NHibernautServer? _current;

    /// <summary>Shared JSON options for the dashboard API (web defaults, camelCase).</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly NHibernautOptions _options;
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;
    private volatile bool _running;

    private NHibernautServer(NHibernautOptions options) => _options = options;

    /// <summary>The base URL the server is listening on.</summary>
    public string Url { get; private set; } = string.Empty;

    /// <summary>Starts (or returns the already-running) dashboard server. Idempotent.</summary>
    public static NHibernautServer Start(NHibernautOptions? options = null)
    {
        lock (Gate)
        {
            if (_current is { _running: true }) return _current;

            var server = new NHibernautServer(options ?? NHibernautRuntime.Options);
            server.StartInternal();
            _current = server;
            return server;
        }
    }

    /// <summary>Stops the running dashboard server, if any.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            _current?.Dispose();
            _current = null;
        }
    }

    private void StartInternal()
    {
        var bind = _options.Dashboard.BindAddress;
        var port = _options.Dashboard.Port;

        if (!IsLoopback(bind) && string.IsNullOrEmpty(_options.Dashboard.AuthToken))
        {
            throw new InvalidOperationException(
                "NHibernaut: refusing to bind a non-loopback address without Dashboard.AuthToken. " +
                "The dashboard exposes SQL and parameter values — set an AuthToken to bind beyond loopback.");
        }

        if (IsLoopback(bind))
        {
            // Register both loopback hosts so http://localhost:port and http://127.0.0.1:port both work
            // (HttpListener matches by Host header, not just the resolved IP).
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add($"http://localhost:{port}/");
            Url = $"http://localhost:{port}/";
        }
        else
        {
            var host = bind is "0.0.0.0" or "::" ? "+" : bind;
            _listener.Prefixes.Add($"http://{host}:{port}/");
            Url = $"http://{bind}:{port}/";
        }

        _listener.Start();
        _running = true;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (_running)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch when (!_running)
            {
                break; // listener stopped
            }
            catch
            {
                continue;
            }

            _ = Task.Run(() => HandleSafe(context));
        }
    }

    private void HandleSafe(HttpListenerContext context)
    {
        try
        {
            Route(context);
        }
        catch (Exception ex)
        {
            NHibernautRuntime.ReportInternalError(ex);
            TryFail(context);
        }
    }

    private void Route(HttpListenerContext context)
    {
        var request = context.Request;

        if (!Authorized(request))
        {
            WriteText(context, 401, "Unauthorized");
            return;
        }

        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod;

        if (method == "GET" && path == "/api/config") { WriteJson(context, 200, new { editorLinkScheme = _options.Dashboard.EditorLinkScheme }); return; }
        if (method == "GET" && path == "/api/sessions") { HandleSessions(context); return; }
        if (method == "GET" && path.StartsWith("/api/sessions/", StringComparison.Ordinal)) { HandleSessionDetail(context, path.Substring("/api/sessions/".Length)); return; }
        if (method == "GET" && path == "/api/aggregate") { HandleAggregate(context); return; }
        if (method == "GET" && path == "/api/alerts") { HandleAlerts(context); return; }
        if (method == "GET" && path == "/api/stream") { HandleStream(context); return; }
        if (method == "DELETE" && path == "/api/sessions") { NHibernautRuntime.Store.Clear(); context.Response.StatusCode = 204; context.Response.Close(); return; }
        if (method == "POST" && path == "/api/ingest") { HandleIngest(context); return; }
        if (method == "GET") { Assets.Serve(context, path); return; }

        WriteText(context, 405, "Method Not Allowed");
    }

    private void HandleSessions(HttpListenerContext context)
    {
        var q = context.Request.QueryString;
        var sessions = DashboardApi.Sessions(ParseInt(q["take"], 50), ParseDate(q["since"]), ParseSeverity(q["minSeverity"]));
        WriteJson(context, 200, sessions);
    }

    private void HandleSessionDetail(HttpListenerContext context, string idText)
    {
        if (!Guid.TryParse(idText, out var id))
        {
            WriteText(context, 400, "Invalid session id");
            return;
        }

        var detail = DashboardApi.SessionDetail(id);
        if (detail is null)
        {
            WriteText(context, 404, "Not found");
            return;
        }

        WriteJson(context, 200, detail);
    }

    private void HandleAggregate(HttpListenerContext context)
        => WriteJson(context, 200, DashboardApi.Aggregate(_options));

    private void HandleAlerts(HttpListenerContext context)
        => WriteJson(context, 200, DashboardApi.Alerts(ParseInt(context.Request.QueryString["take"], 100)));

    private void HandleStream(HttpListenerContext context)
    {
        var response = context.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.SendChunked = true;

        var store = NHibernautRuntime.Store;
        var queue = new ConcurrentQueue<string>();
        using var signal = new SemaphoreSlim(0);

        void OnSealed(ProfiledSession s)
        {
            try
            {
                var json = JsonSerializer.Serialize(DtoMapper.ToSummary(s), JsonOptions);
                queue.Enqueue(json);
                signal.Release();
            }
            catch (Exception ex) { NHibernautRuntime.ReportInternalError(ex); }
        }

        store.SessionSealed += OnSealed;
        try
        {
            WriteRaw(response, ": connected\n\n");

            while (_running && !_cts.IsCancellationRequested)
            {
                bool got;
                try { got = signal.Wait(TimeSpan.FromSeconds(10), _cts.Token); }
                catch (OperationCanceledException) { break; }

                if (got)
                {
                    while (queue.TryDequeue(out var json))
                        WriteRaw(response, $"event: session\ndata: {json}\n\n");
                }
                else
                {
                    WriteRaw(response, ": ping\n\n"); // heartbeat; throws if client disconnected
                }
            }
        }
        catch
        {
            // client disconnected or server stopping — fall through to cleanup
        }
        finally
        {
            store.SessionSealed -= OnSealed;
            try { response.Close(); } catch { /* ignore */ }
        }
    }

    private void HandleIngest(HttpListenerContext context)
    {
        SessionDetailDto? dto;
        try
        {
            using var reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8);
            dto = JsonSerializer.Deserialize<SessionDetailDto>(reader.ReadToEnd(), JsonOptions);
        }
        catch (Exception ex)
        {
            NHibernautRuntime.ReportInternalError(ex);
            WriteText(context, 400, "Invalid session payload");
            return;
        }

        if (dto is null)
        {
            WriteText(context, 400, "Empty session payload");
            return;
        }

        try
        {
            DashboardApi.Ingest(dto);
        }
        catch (Exception ex)
        {
            NHibernautRuntime.ReportInternalError(ex);
            WriteText(context, 400, "Invalid session payload");
            return;
        }

        context.Response.StatusCode = 202;
        context.Response.Close();
    }

    private bool Authorized(HttpListenerRequest request)
    {
        var token = _options.Dashboard.AuthToken;
        if (string.IsNullOrEmpty(token)) return true; // loopback dev: no token configured

        var provided = request.Headers["X-NHibernaut-Token"] ?? request.QueryString["token"];
        return string.Equals(provided, token, StringComparison.Ordinal);
    }

    // ---- response helpers ----

    private static void WriteJson(HttpListenerContext context, int status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static void WriteText(HttpListenerContext context, int status, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.Close();
    }

    private static void WriteRaw(HttpListenerResponse response, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Flush();
    }

    private static void TryFail(HttpListenerContext context)
    {
        try { WriteText(context, 500, "Internal Server Error"); }
        catch { /* response may already be committed */ }
    }

    // ---- parsing ----

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, out var n) && n > 0 ? n : fallback;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var d) ? d : null;

    private static AlertSeverity? ParseSeverity(string? value)
        => Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var s) ? s : null;

    private static bool IsLoopback(string address)
    {
        if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var trimmed = address.Trim('[', ']');
        return IPAddress.TryParse(trimmed, out var ip) && IPAddress.IsLoopback(ip);
    }

    public void Dispose()
    {
        if (!_running && _acceptLoop is null) return;
        _running = false;
        try { _cts.Cancel(); } catch { /* ignore */ }
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
        _cts.Dispose();

        lock (Gate)
        {
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }
}

public sealed record AlertFeedItemDto(string SessionId, DateTimeOffset SessionStartedAt, AlertDto Alert);
