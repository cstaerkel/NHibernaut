using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using NHibernaut.Client;

namespace NHibernaut.App.ViewModels;

public partial class CompareViewModel : ViewModelBase
{
    private readonly ILogger<CompareViewModel>? _log;
    private IDashboardClient? _client;
    private int _compareGeneration;

    public CompareViewModel(ILogger<CompareViewModel>? log = null) => _log = log;

    public ObservableCollection<SessionItemViewModel> Options { get; } = new();
    public ObservableCollection<CompareRow> Rows { get; } = new();

    [ObservableProperty] private SessionItemViewModel? _selectedA;
    [ObservableProperty] private SessionItemViewModel? _selectedB;

    public async Task LoadAsync(IDashboardClient client)
    {
        _client = client;
        // Fresh connection: drop any prior comparison so selections/rows can't cross dashboards.
        SelectedA = null;
        SelectedB = null;
        Rows.Clear();
        Options.Clear();
        foreach (var s in await client.GetSessionsAsync())
            Options.Add(new SessionItemViewModel(s));
    }

    /// <summary>
    /// Refresh the options list from the client while PRESERVING the user's current A/B selection by
    /// session id: if a selected id still exists, re-select its new option instance; otherwise drop it.
    /// </summary>
    public async Task RefreshOptionsAsync(IDashboardClient client)
    {
        _client = client;
        var selectedAId = SelectedA?.Id;
        var selectedBId = SelectedB?.Id;

        Options.Clear();
        foreach (var s in await client.GetSessionsAsync())
            Options.Add(new SessionItemViewModel(s));

        // Re-resolve selections to the new instances (POCO: clearing Options doesn't null the bound props).
        SelectedA = selectedAId is { } a ? Options.FirstOrDefault(o => o.Id == a) : null;
        SelectedB = selectedBId is { } b ? Options.FirstOrDefault(o => o.Id == b) : null;
    }

    /// <summary>Clears options, rows and selections (used by the shell's Clear-all). No client call.</summary>
    public void Clear()
    {
        SelectedA = null;
        SelectedB = null;
        Options.Clear();
        Rows.Clear();
    }

    partial void OnSelectedAChanged(SessionItemViewModel? value) => _ = CompareAsync();
    partial void OnSelectedBChanged(SessionItemViewModel? value) => _ = CompareAsync();

    private async Task CompareAsync()
    {
        var gen = ++_compareGeneration;          // UI-thread single-threaded; newest run wins
        Rows.Clear();
        if (_client is null || SelectedA is null || SelectedB is null) return;
        try
        {
            var results = await Task.WhenAll(
                _client.GetSessionDetailAsync(SelectedA.Id),
                _client.GetSessionDetailAsync(SelectedB.Id));
            if (_compareGeneration != gen) return;   // a newer selection superseded this run
            var a = results[0];
            var b = results[1];
            if (a is null || b is null) return;
            Add("Queries", a.Summary.StatementCount, b.Summary.StatementCount);
            Add("Total ms", a.Summary.TotalDurationMs, b.Summary.TotalDurationMs);
            Add("Rows read", a.Summary.TotalRowsRead, b.Summary.TotalRowsRead);
            Add("Writes", a.Summary.WriteCount, b.Summary.WriteCount);
            Add("Alerts", a.Summary.AlertCount, b.Summary.AlertCount);
        }
        catch (Exception ex) { _log?.LogWarning(ex, "Compare failed"); }
    }

    private void Add(string metric, double a, double b) => Rows.Add(new CompareRow(metric, a, b));
}
