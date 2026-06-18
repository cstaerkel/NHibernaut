using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NHibernaut.Client;
using NHibernaut.Server;

namespace NHibernaut.App.ViewModels;

public partial class SessionsViewModel : ViewModelBase
{
    /// <summary>Upper bound on retained live sessions; mirrors the backend's bounded store.</summary>
    public const int MaxSessions = 500;

    private IDashboardClient? _client;
    private readonly Func<SessionDetailViewModel>? _detailFactory;

    public SessionsViewModel(Func<SessionDetailViewModel>? detailFactory = null) => _detailFactory = detailFactory;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = new();

    [ObservableProperty] private SessionItemViewModel? _selected;
    [ObservableProperty] private bool _live = true;
    [ObservableProperty] private int _totalQueries;
    [ObservableProperty] private double _totalMs;
    [ObservableProperty] private int _alertedSessions;
    [ObservableProperty] private SessionDetailViewModel? _detail;

    partial void OnSelectedChanged(SessionItemViewModel? value)
    {
        if (value is null || _client is null || _detailFactory is null) { Detail = null; return; }
        var vm = _detailFactory();
        Detail = vm;
        _ = LoadDetailSafelyAsync(vm, value.Id);
    }

    private async Task LoadDetailSafelyAsync(SessionDetailViewModel vm, Guid id)
    {
        try { await vm.LoadAsync(_client!, id); } catch { /* detail load failure must not crash */ }
    }

    public async Task LoadAsync(IDashboardClient client)
    {
        _client = client;
        Selected = null;          // drop any stale selection/detail from a previous connection
        Sessions.Clear();
        foreach (var dto in await client.GetSessionsAsync())
            Sessions.Add(new SessionItemViewModel(dto));
        Sort();
        Trim();
        Recount();
    }

    /// <summary>Upsert a live session summary by id, then re-sort (Observer handler).</summary>
    public void ApplyLiveSession(SessionSummaryDto dto)
    {
        if (!Live) return;
        var existing = Sessions.FirstOrDefault(s => s.Dto.Id == dto.Id);
        if (existing is not null) Sessions.Remove(existing);
        Sessions.Insert(0, new SessionItemViewModel(dto));
        Sort();
        Trim();
        Recount();
    }

    /// <summary>Clears the in-memory list without touching the backend (used by the shell's Clear-all).</summary>
    public void ClearLocal()
    {
        Selected = null;          // also clears Detail/StatementPanel via OnSelectedChanged
        Sessions.Clear();
        Recount();
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        if (_client is null) return;
        await _client.ClearAsync();
        Selected = null;
        Sessions.Clear();
        Recount();
    }

    private void Sort()
    {
        var ordered = Sessions.OrderByDescending(s => s.SeverityRank).ThenByDescending(s => s.Dto.StartedAt).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var cur = Sessions.IndexOf(ordered[i]);
            if (cur != i) Sessions.Move(cur, i);
        }
    }

    /// <summary>Evict the lowest-priority entries past the cap (post-sort, the tail is oldest/least severe).</summary>
    private void Trim()
    {
        while (Sessions.Count > MaxSessions)
            Sessions.RemoveAt(Sessions.Count - 1);
    }

    private void Recount()
    {
        TotalQueries = Sessions.Sum(s => s.Dto.StatementCount);
        TotalMs = Sessions.Sum(s => s.Dto.TotalDurationMs);
        AlertedSessions = Sessions.Count(s => s.Dto.MaxSeverity is not null);
    }
}
