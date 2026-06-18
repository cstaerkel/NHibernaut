using System.Threading;
using System.Threading.Tasks;

namespace NHibernaut.Client;

public interface IDashboardClientFactory
{
    Task<IDashboardClient> CreateAsync(DashboardConnection connection, CancellationToken ct = default);
}
