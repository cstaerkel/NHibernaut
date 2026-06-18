using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NHibernaut.Client;

public sealed class DashboardClientFactory : IDashboardClientFactory
{
    private readonly IHttpClientFactory? _httpFactory;
    public DashboardClientFactory(IHttpClientFactory? httpFactory = null) => _httpFactory = httpFactory;

    public async Task<IDashboardClient> CreateAsync(DashboardConnection connection, CancellationToken ct = default)
    {
        if (connection.Mode == DashboardMode.Embedded)
        {
            var c = new InProcessDashboardClient(connection);
            await c.StartAsync(ct).ConfigureAwait(false);
            return c;
        }
        var http = _httpFactory?.CreateClient("dashboard");
        return new HttpDashboardClient(connection, http);
    }
}
