using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using NHibernaut.Core;
using NHibernaut.Server;

namespace NHibernaut.AspNetCore;

/// <summary>
/// Scopes each request for NHibernaut (Tier C): tags sessions with a request id, emits
/// <c>X-NHibernaut-RequestId</c>, and adds a <c>Server-Timing</c> header with this request's total DB
/// time and query count so browser devtools / SPA clients see per-call DB cost. No-op in Production
/// unless <c>Dashboard.EnabledInProduction</c> is set.
/// </summary>
public sealed class NHibernautMiddleware
{
    private readonly RequestDelegate _next;
    private readonly NHibernautOptions _options;
    private readonly bool _isProduction;

    public NHibernautMiddleware(RequestDelegate next, NHibernautOptions options, IHostEnvironment environment)
    {
        _next = next;
        _options = options;
        _isProduction = environment.IsProduction();
    }

    public async Task Invoke(HttpContext context)
    {
        if (_isProduction && !_options.Dashboard.EnabledInProduction)
        {
            await _next(context);
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        NHibernautRequestContext.CurrentRequestId = requestId;
        context.Response.Headers["X-NHibernaut-RequestId"] = requestId;

        context.Response.OnStarting(() =>
        {
            // Computed just before the response begins, by which point the request's NHibernate work
            // has executed. Uses the captured request id (the AsyncLocal is cleared by then).
            var (durationMs, queries) = DashboardApi.RequestCost(requestId);
            context.Response.Headers["Server-Timing"] =
                $"db;dur={durationMs.ToString("F1", CultureInfo.InvariantCulture)};desc=\"NHibernaut {queries} queries\"";
            return Task.CompletedTask;
        });

        try
        {
            await _next(context);
        }
        finally
        {
            NHibernautRequestContext.CurrentRequestId = null;
        }
    }
}
