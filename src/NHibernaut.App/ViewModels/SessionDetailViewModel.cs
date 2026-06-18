using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NHibernaut.App.Services;
using NHibernaut.Client;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public partial class SessionDetailViewModel : ViewModelBase
{
    private readonly Dictionary<Guid, StatementViewModel> _byId = new();
    private readonly Dictionary<Guid, StatementDto> _stmtById = new();
    private readonly IEditorLinkService? _editorLinks;
    private readonly EditorSchemeProvider? _scheme;
    private SessionDetailDto? _dto;

    public ObservableCollection<AlertViewModel> Alerts { get; } = new();
    public ObservableCollection<WaterfallBarViewModel> Bars { get; } = new();
    public ObservableCollection<StatementGroupViewModel> Groups { get; } = new();

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private StatementViewModel? _selectedStatement;
    [ObservableProperty] private StatementDetailViewModel? _statementPanel;

    public SessionDetailViewModel(IEditorLinkService? editorLinks = null, EditorSchemeProvider? scheme = null)
    {
        _editorLinks = editorLinks;
        _scheme = scheme;
    }

    public async Task LoadAsync(IDashboardClient client, Guid id)
    {
        var detail = await client.GetSessionDetailAsync(id);
        Build(detail);
    }

    /// <summary>Public for unit testing (build the VM tree from a DTO, no client needed).</summary>
    public void Build(SessionDetailDto? detail)
    {
        _dto = detail;
        Alerts.Clear(); Bars.Clear(); Groups.Clear(); _byId.Clear(); _stmtById.Clear();
        SelectedStatement = null; StatementPanel = null;
        if (detail is null) { Title = ""; return; }

        Title = $"{detail.Summary.Id[..Math.Min(8, detail.Summary.Id.Length)]} · {detail.Summary.StatementCount} queries · {detail.Summary.TotalDurationMs:F0} ms";

        foreach (var a in detail.Alerts) Alerts.Add(new AlertViewModel(a));

        var statements = detail.Statements;
        if (statements.Count > 0)
        {
            var t0 = statements.Min(s => s.StartedAt.ToUnixTimeMilliseconds());
            var t1 = statements.Max(s => s.StartedAt.ToUnixTimeMilliseconds() + (long)s.DurationMs);
            double span = Math.Max(t1 - t0, 1);
            foreach (var s in statements)
            {
                var left = (s.StartedAt.ToUnixTimeMilliseconds() - t0) / span * 100.0;
                var width = Math.Max(s.DurationMs / span * 100.0, 0.6);
                Bars.Add(new WaterfallBarViewModel(s, left, width));
            }
        }

        StatementGroupViewModel? cur = null;
        foreach (var s in statements)
        {
            var vm = new StatementViewModel(s);
            _byId[vm.Id] = vm;
            _stmtById[vm.Id] = s;
            if (cur is not null && cur.Key == s.NormalizedSql) cur.Items.Add(vm);
            else { cur = new StatementGroupViewModel(s.NormalizedSql); cur.Items.Add(vm); Groups.Add(cur); }
        }
    }

    [RelayCommand]
    private void SelectStatement(Guid id)
    {
        if (_byId.TryGetValue(id, out var vm)) SelectedStatement = vm;
        if (_dto is not null && _stmtById.TryGetValue(id, out var dto))
            StatementPanel = new StatementDetailViewModel(dto, _dto, _scheme?.Scheme ?? "vscode", _editorLinks);
        else
            StatementPanel = null;
    }

    [RelayCommand] private void HighlightAlert(AlertViewModel alert)
    {
        var ids = new HashSet<string>(alert.RelatedStatementIds);
        foreach (var bar in Bars) bar.IsHighlighted = ids.Contains(bar.StatementId.ToString());
    }
}
