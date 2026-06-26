using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NHibernaut.Client;
using NHibernaut.Mcp.Configuration;
using NHibernaut.Mcp.Formatting;
using NHibernaut.Mcp.Profiling;
using NHibernaut.Mcp.Prompts;
using NHibernaut.Mcp.Resources;
using NHibernaut.Mcp.Tools;

namespace NHibernaut.Mcp.Hosting;

public static class NHibernautMcpHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
        => CreateBuilder(args, Environment.GetEnvironmentVariable);

    public static HostApplicationBuilder CreateBuilder(string[] args, Func<string, string?> environment)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ApplicationName = "nhibernaut",
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        var options = NHibernautMcpOptionsProvider.Resolve(args, environment);
        builder.Services.AddSingleton(options);
        builder.Services.AddHttpClient("dashboard", http =>
        {
            http.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
        });
        builder.Services.AddSingleton<IDashboardClientFactory, DashboardClientFactory>();
        builder.Services.AddSingleton(sp => DashboardConnection.Remote(options.DashboardUrl, options.DashboardToken));
        builder.Services.AddSingleton<IDashboardClient>(sp =>
        {
            var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
            return new HttpDashboardClient(
                sp.GetRequiredService<DashboardConnection>(),
                httpFactory.CreateClient("dashboard"));
        });
        builder.Services.AddSingleton<IProfilerQueryService, ProfilerQueryService>();
        builder.Services.AddSingleton<McpResponseFormatter>();

        builder.Services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "nhibernaut",
                    Title = "nhibernaut",
                    Version = typeof(NHibernautMcpHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                };
                o.ServerInstructions =
                    "Use NHibernaut MCP tools to inspect an existing NHibernate profiler dashboard. " +
                    "All initial operations are read-only. SQL text is available by default; parameter values " +
                    "and stack traces require explicit request flags plus NHIBERNAUT_MCP_ALLOW_SENSITIVE=1.";
            })
            .WithStdioServerTransport()
            .WithTools<NHibernautMcpTools>()
            .WithResources<NHibernautMcpResources>()
            .WithPrompts<NHibernautMcpPrompts>();

        return builder;
    }

    public static async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        using var host = CreateBuilder(args).Build();
        host.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("NHibernaut.Mcp")
            .LogInformation("NHibernaut MCP server starting.");

        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
