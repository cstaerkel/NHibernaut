using System.Threading;

namespace NHibernaut.Core;

/// <summary>
/// Ambient request correlation (Tier C). The ASP.NET Core middleware sets the current request id at
/// the start of a request; sessions created during that flow are stamped with it so per-request DB
/// cost (Server-Timing) can be computed and the dashboard can group by request.
/// </summary>
public static class NHibernautRequestContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CurrentRequestId
    {
        get => Current.Value;
        set => Current.Value = value;
    }
}
