using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using NHibernaut.Mcp.Hosting;
using Xunit;

namespace NHibernaut.Mcp.Tests.Hosting;

public sealed class StdioLoggingTests
{
    [Fact]
    public void Logging_configuration_uses_single_stderr_console_provider()
    {
        var builder = NHibernautMcpHost.CreateBuilder([], _ => null);
        using var host = builder.Build();
        var loggerProviders = host.Services.GetServices<ILoggerProvider>().ToList();

        Assert.Single(loggerProviders);
        Assert.Contains("ConsoleLoggerProvider", loggerProviders[0].GetType().Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Console_logger_threshold_sends_all_diagnostics_to_stderr()
    {
        var builder = NHibernautMcpHost.CreateBuilder([], _ => null);
        using var host = builder.Build();
        var options = host.Services
            .GetRequiredService<IOptionsMonitor<ConsoleLoggerOptions>>()
            .CurrentValue;

        Assert.Equal(LogLevel.Trace, options.LogToStandardErrorThreshold);
    }

    [Fact]
    public void Test_process_can_capture_stdout_and_stderr_separately()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            Console.Out.Write("protocol");
            Console.Error.Write("diagnostic");

            Assert.Equal("protocol", stdout.ToString());
            Assert.Equal("diagnostic", stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
