using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NHibernaut.App.ViewModels;

public partial class StatementGroupViewModel : ViewModelBase
{
    public StatementGroupViewModel(string? key) { Key = key; }
    public string? Key { get; }
    public ObservableCollection<StatementViewModel> Items { get; } = new();
    public int Count => Items.Count;
    public bool IsGroup => Items.Count > 1;
    public double TotalMs => Items.Sum(s => s.DurationMs);
    public string Header => IsGroup ? $"×{Count}  {Items[0].SqlFirstLine}" : Items[0].SqlFirstLine;

    [ObservableProperty] private bool _isFolded = true;   // groups collapsed by default

    partial void OnIsFoldedChanged(bool value) => OnPropertyChanged(nameof(ShowItems));

    /// <summary>Single items always show; groups show only when unfolded.</summary>
    public bool ShowItems => !IsGroup || !IsFolded;
}
