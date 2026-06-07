using System;
using System.Collections.Generic;
using System.Linq;
using NHibernaut.Core;
using NHibernaut.Core.Model;

namespace NHibernaut.Server;

/// <summary>
/// Transport-agnostic dashboard query logic over <see cref="NHibernautRuntime.Store"/>. Used by both
/// the HttpListener server and the ASP.NET Core (Tier C) endpoints so they expose an identical API.
/// </summary>
public static class DashboardApi
{
    public static IReadOnlyList<SessionSummaryDto> Sessions(int take, DateTimeOffset? since, AlertSeverity? minSeverity)
    {
        return NHibernautRuntime.Store.GetRecentSessions(Math.Max(take, 1000))
            .Where(s => since is null || s.StartedAt >= since)
            .Select(DtoMapper.ToSummary)
            .Where(s => minSeverity is null || (ParseSeverity(s.MaxSeverity) ?? AlertSeverity.Info) >= minSeverity)
            .Take(take)
            .ToList();
    }

    public static SessionDetailDto? SessionDetail(Guid id)
    {
        var session = NHibernautRuntime.Store.GetSession(id);
        return session is null ? null : DtoMapper.ToDetail(session);
    }

    public static IReadOnlyList<AggregateRowDto> Aggregate(NHibernautOptions options)
        => DtoMapper.ToAggregate(NHibernautRuntime.Store.GetRecentSessions(int.MaxValue), options);

    public static IReadOnlyList<AlertFeedItemDto> Alerts(int take)
    {
        return NHibernautRuntime.Store.GetRecentSessions(int.MaxValue)
            .SelectMany(s =>
            {
                lock (s.SyncRoot)
                {
                    return s.Alerts.Select(a => new AlertFeedItemDto(s.Id.ToString(), s.StartedAt, DtoMapper.ToAlert(a))).ToList();
                }
            })
            .OrderByDescending(x => x.SessionStartedAt)
            .Take(take)
            .ToList();
    }

    public static void Clear() => NHibernautRuntime.Store.Clear();

    /// <summary>Maps a session to its summary DTO (used by the SSE live feed).</summary>
    public static SessionSummaryDto Summarize(ProfiledSession session) => DtoMapper.ToSummary(session);

    /// <summary>Total DB time (ms) and statement count for the sessions tagged with a request id (Server-Timing).</summary>
    public static (double DurationMs, int Queries) RequestCost(string requestId)
    {
        double ms = 0;
        var queries = 0;
        foreach (var s in NHibernautRuntime.Store.GetRecentSessions(int.MaxValue))
        {
            if (s.RequestId != requestId) continue;
            lock (s.SyncRoot)
            {
                ms += s.TotalDurationMs;
                queries += s.StatementCount;
            }
        }
        return (ms, queries);
    }

    public static AlertSeverity? ParseSeverity(string? value)
        => Enum.TryParse<AlertSeverity>(value, ignoreCase: true, out var s) ? s : null;
}
