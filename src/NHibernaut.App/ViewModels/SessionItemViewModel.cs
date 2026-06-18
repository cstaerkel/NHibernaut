using System;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public sealed class SessionItemViewModel
{
    public SessionSummaryDto Dto { get; }
    public SessionItemViewModel(SessionSummaryDto dto) => Dto = dto;

    public Guid Id => Guid.Parse(Dto.Id);
    public string ShortId => Dto.Id.Length >= 8 ? Dto.Id[..8] : Dto.Id;
    public string Severity => Dto.MaxSeverity ?? "None";
    public int SeverityRank => Dto.MaxSeverity switch { "Error" => 3, "Warning" => 2, "Info" => 1, _ => 0 };
    public string Summary => $"{Dto.StatementCount} queries · {Dto.TotalDurationMs:F0} ms";
    public string Started => Dto.StartedAt.LocalDateTime.ToString("HH:mm:ss") + (Dto.IsSealed ? "" : " · open");
    public string Meta => $"{Dto.AlertCount} alert{(Dto.AlertCount == 1 ? "" : "s")} · {Dto.TotalRowsRead} rows · {Dto.WriteCount} writes";
    public string CompareLabel => $"{ShortId} · {Dto.StatementCount}q · {Dto.TotalDurationMs:F0}ms";

    /// <summary>Screen-reader label for the session list row (otherwise the row reads as the VM type name).</summary>
    public string AccessibleLabel => $"Session {ShortId}, {Summary}, started {Started}, {Meta}";
}
