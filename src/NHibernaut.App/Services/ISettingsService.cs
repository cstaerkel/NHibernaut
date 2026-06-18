using System.Threading.Tasks;

namespace NHibernaut.App.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync();
    Task SaveAsync();
}
