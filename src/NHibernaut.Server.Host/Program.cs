using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernaut.Server.Host;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Pin the content root to the install directory. Service managers launch with a working
    // directory that is NOT the app's: systemd and launchd use "/", the Windows SCM uses
    // System32. The default content root is the working directory, and the host's appsettings
    // file provider (reloadOnChange) sets up a recursive inotify watch over the entire content
    // root at startup — rooted at "/", that enumerates and watches the whole filesystem (tens of
    // thousands of inotify watches), stalling service start by 30-90s and tripping systemd's 90s
    // start timeout. AppContext.BaseDirectory is the (small) folder the binary was installed to —
    // for a single-file app it is the executable's directory, not the extraction temp.
    ContentRootPath = AppContext.BaseDirectory,
});

// Integrate with the OS service manager when run as a service; no-ops otherwise.
builder.Services.AddWindowsService(o => o.ServiceName = "NHibernautDashboard");
builder.Services.AddSystemd();

var config = DashboardHostOptions.Resolve(
    Environment.GetEnvironmentVariable,
    DashboardHostOptions.GenerateToken);

builder.Services.AddSingleton(config);
builder.Services.AddHostedService<DashboardHostedService>();

builder.Build().Run();
