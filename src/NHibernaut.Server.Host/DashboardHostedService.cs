using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NHibernaut.Core;
using NHibernaut.Server;

namespace NHibernaut.Server.Host;

/// <summary>Starts the in-process <see cref="NHibernautServer"/> for the process lifetime.</summary>
public sealed class DashboardHostedService : BackgroundService
{
    private readonly DashboardHostOptions _config;
    private readonly ILogger<DashboardHostedService> _log;

    public DashboardHostedService(DashboardHostOptions config, ILogger<DashboardHostedService> log)
    {
        _config = config;
        _log = log;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NHibernautRuntime.Options.Dashboard.BindAddress = _config.BindAddress;
        NHibernautRuntime.Options.Dashboard.Port = _config.Port;
        NHibernautRuntime.Options.Dashboard.AuthToken = _config.AuthToken;

        var server = NHibernautServer.Start(NHibernautRuntime.Options);
        _log.LogInformation("NHibernaut dashboard listening on {Url}", server.Url);
        if (_config.TokenWasGenerated)
            _log.LogWarning("Generated auth token (required for non-loopback bind): {Token}", _config.AuthToken);

        // NHibernautServer runs its own accept loop; nothing to do until shutdown.
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        NHibernautServer.Stop();
        await base.StopAsync(cancellationToken);
    }
}
