using System.Collections.ObjectModel;
using System.Threading.Tasks;
using NHibernaut.Client;

namespace NHibernaut.App.ViewModels;

public sealed class AggregateViewModel : ViewModelBase
{
    public ObservableCollection<AggregateRowViewModel> Rows { get; } = new();

    public async Task LoadAsync(IDashboardClient client)
    {
        Rows.Clear();
        foreach (var dto in await client.GetAggregateAsync())   // already sorted by total time desc — preserve order
            Rows.Add(new AggregateRowViewModel(dto));
    }
}
