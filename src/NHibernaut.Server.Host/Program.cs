using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernaut.Server.Host;

var builder = Host.CreateApplicationBuilder(args);

// Integrate with the OS service manager when run as a service; no-ops otherwise.
builder.Services.AddWindowsService(o => o.ServiceName = "NHibernautDashboard");
builder.Services.AddSystemd();

var config = DashboardHostOptions.Resolve(
    Environment.GetEnvironmentVariable,
    DashboardHostOptions.GenerateToken);

builder.Services.AddSingleton(config);
builder.Services.AddHostedService<DashboardHostedService>();

builder.Build().Run();
