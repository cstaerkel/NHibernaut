using System.Collections.Generic;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public sealed class AlertViewModel
{
    public AlertViewModel(AlertDto dto) => Dto = dto;

    public AlertDto Dto { get; }
    public string Title => Dto.Title;
    public string Severity => Dto.Severity;
    public string Description => Dto.Description;
    public string? Suggestion => Dto.Suggestion;
    public IReadOnlyList<string> RelatedStatementIds => Dto.RelatedStatementIds;
}
