using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.Client;

/// <summary>Talks to a remote NHibernautServer over its JSON + SSE API, reusing the wire DTOs.</summary>
public sealed class HttpDashboardClient : IDashboardClient
{
    private const string DefaultEditorScheme = "vscode";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Uri _base;
    private readonly string? _token;

    public DashboardConnection Connection { get; }

    public HttpDashboardClient(DashboardConnection connection, HttpClient? http = null)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _http = http ?? new HttpClient();
        _ownsHttp = http is null;
        // Build per-request messages instead of mutating the (possibly shared / factory-managed) HttpClient.
        _base = new Uri(connection.Url.EndsWith('/') ? connection.Url : connection.Url + "/");
        _token = connection.Token;
    }

    public async Task<DashboardConfig> GetConfigAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "api/config");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<DashboardConfig>(NHibernautServer.JsonOptions, ct).ConfigureAwait(false);
        // Older servers may omit editorLinkScheme; fall back to the default.
        return new DashboardConfig(dto?.EditorLinkScheme ?? DefaultEditorScheme);
    }

    public async Task<IReadOnlyList<SessionSummaryDto>> GetSessionsAsync(int take = 500, DateTimeOffset? since = null, AlertSeverity? minSeverity = null, CancellationToken ct = default)
    {
        var url = $"api/sessions?take={take}";
        if (since is { } s) url += $"&since={Uri.EscapeDataString(s.ToString("o"))}";
        if (minSeverity is { } sev) url += $"&minSeverity={sev}";
        using var req = Request(HttpMethod.Get, url);
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<SessionSummaryDto>>(NHibernautServer.JsonOptions, ct).ConfigureAwait(false)
               ?? new List<SessionSummaryDto>();
    }

    public async Task<SessionDetailDto?> GetSessionDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, $"api/sessions/{id}");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<SessionDetailDto>(NHibernautServer.JsonOptions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AggregateRowDto>> GetAggregateAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "api/aggregate");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AggregateRowDto>>(NHibernautServer.JsonOptions, ct).ConfigureAwait(false)
               ?? new List<AggregateRowDto>();
    }

    public async Task<IReadOnlyList<AlertFeedItemDto>> GetAlertsAsync(int take = 100, CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, $"api/alerts?take={take}");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<AlertFeedItemDto>>(NHibernautServer.JsonOptions, ct).ConfigureAwait(false)
               ?? new List<AlertFeedItemDto>();
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Delete, "api/sessions");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    public async IAsyncEnumerable<SessionSummaryDto> StreamSessionsAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        using var req = Request(HttpMethod.Get, "api/stream");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        await foreach (var (evt, data) in SseReader.ReadAsync(stream, ct).ConfigureAwait(false))
        {
            if (evt != "session") continue;
            SessionSummaryDto? dto = null;
            try { dto = System.Text.Json.JsonSerializer.Deserialize<SessionSummaryDto>(data, NHibernautServer.JsonOptions); }
            catch { /* skip malformed frame */ }
            if (dto is not null) yield return dto;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_ownsHttp) _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private HttpRequestMessage Request(HttpMethod method, string relativeUrl)
    {
        var req = new HttpRequestMessage(method, new Uri(_base, relativeUrl));
        if (!string.IsNullOrEmpty(_token))
            req.Headers.TryAddWithoutValidation("X-NHibernaut-Token", _token);
        return req;
    }
}
