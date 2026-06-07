using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NHibernaut.Core;
using NHibernaut.Core.Model;
using NHibernaut.Server;

namespace NHibernaut.AspNetCore;

/// <summary>
/// Serves the same dashboard API + embedded SPA as the HttpListener server, but over the host's
/// ASP.NET Core pipeline (Tier C). Mounted under a path by <c>MapNHibernaut</c>.
/// </summary>
internal static class DashboardEndpoints
{
    public static async Task HandleAsync(HttpContext ctx, NHibernautOptions options, IHostEnvironment env, string mountPath)
    {
        // Don't expose the dashboard (SQL + parameter values) in Production unless explicitly enabled.
        if (env.IsProduction() && !options.Dashboard.EnabledInProduction)
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!Authorized(ctx, options))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }

        var path = ctx.Request.Path.Value ?? "/";
        var method = ctx.Request.Method;

        // Ensure a trailing slash so the SPA's relative asset/base-path resolution works under the mount.
        if (path.Length == 0)
        {
            ctx.Response.Redirect(mountPath + "/");
            return;
        }

        if (method == "GET" && path == "/api/config")
        {
            await ctx.Response.WriteAsJsonAsync(new { editorLinkScheme = options.Dashboard.EditorLinkScheme }, NHibernautServer.JsonOptions);
            return;
        }
        if (method == "GET" && path == "/api/sessions")
        {
            await WriteJson(ctx, DashboardApi.Sessions(QueryInt(ctx, "take", 50), QueryDate(ctx, "since"), DashboardApi.ParseSeverity(ctx.Request.Query["minSeverity"])));
            return;
        }
        if (method == "GET" && path.StartsWith("/api/sessions/", StringComparison.Ordinal))
        {
            var idText = path.Substring("/api/sessions/".Length);
            if (!Guid.TryParse(idText, out var id)) { ctx.Response.StatusCode = 400; return; }
            var detail = DashboardApi.SessionDetail(id);
            if (detail is null) { ctx.Response.StatusCode = 404; return; }
            await WriteJson(ctx, detail);
            return;
        }
        if (method == "GET" && path == "/api/aggregate") { await WriteJson(ctx, DashboardApi.Aggregate(options)); return; }
        if (method == "GET" && path == "/api/alerts") { await WriteJson(ctx, DashboardApi.Alerts(QueryInt(ctx, "take", 100))); return; }
        if (method == "GET" && path == "/api/stream") { await StreamAsync(ctx); return; }
        if (method == "DELETE" && path == "/api/sessions") { DashboardApi.Clear(); ctx.Response.StatusCode = 204; return; }
        if (method == "GET") { await ServeAssetAsync(ctx, path); return; }

        ctx.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
    }

    private static bool Authorized(HttpContext ctx, NHibernautOptions options)
    {
        var token = options.Dashboard.AuthToken;
        if (string.IsNullOrEmpty(token)) return true;
        var provided = ctx.Request.Headers["X-NHibernaut-Token"].ToString();
        if (string.IsNullOrEmpty(provided)) provided = ctx.Request.Query["token"].ToString();
        return string.Equals(provided, token, StringComparison.Ordinal);
    }

    private static async Task WriteJson(HttpContext ctx, object payload)
        => await ctx.Response.WriteAsJsonAsync(payload, NHibernautServer.JsonOptions);

    private static async Task ServeAssetAsync(HttpContext ctx, string path)
    {
        if (Assets.TryGet(path, out var bytes, out var contentType))
        {
            ctx.Response.ContentType = contentType;
            await ctx.Response.Body.WriteAsync(bytes);
        }
        else
        {
            ctx.Response.StatusCode = 404;
        }
    }

    private static async Task StreamAsync(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";

        var store = NHibernautRuntime.Store;
        var queue = new ConcurrentQueue<string>();
        using var signal = new SemaphoreSlim(0);

        void OnSealed(ProfiledSession s)
        {
            try
            {
                queue.Enqueue(JsonSerializer.Serialize(DashboardApi.Summarize(s), NHibernautServer.JsonOptions));
                signal.Release();
            }
            catch (Exception ex) { NHibernautRuntime.ReportInternalError(ex); }
        }

        store.SessionSealed += OnSealed;
        try
        {
            await ctx.Response.WriteAsync(": connected\n\n");
            await ctx.Response.Body.FlushAsync();

            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                bool got;
                try { got = await signal.WaitAsync(TimeSpan.FromSeconds(10), ctx.RequestAborted); }
                catch (OperationCanceledException) { break; }

                if (got)
                {
                    while (queue.TryDequeue(out var json))
                        await ctx.Response.WriteAsync($"event: session\ndata: {json}\n\n");
                }
                else
                {
                    await ctx.Response.WriteAsync(": ping\n\n");
                }
                await ctx.Response.Body.FlushAsync();
            }
        }
        catch
        {
            // client disconnected
        }
        finally
        {
            store.SessionSealed -= OnSealed;
        }
    }

    private static int QueryInt(HttpContext ctx, string key, int fallback)
        => int.TryParse(ctx.Request.Query[key], out var n) && n > 0 ? n : fallback;

    private static DateTimeOffset? QueryDate(HttpContext ctx, string key)
        => DateTimeOffset.TryParse(ctx.Request.Query[key], out var d) ? d : null;
}
