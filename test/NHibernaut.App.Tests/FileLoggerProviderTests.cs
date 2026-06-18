using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using NHibernaut.App.Logging;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public void WritesInfoLineToLogFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);

        try
        {
            var options = new FileLoggerOptions
            {
                Directory = dir,
                MinLevel = LogLevel.Information,
            };

            string logContent;
            using (var provider = new FileLoggerProvider(options))
            {
                var logger = provider.CreateLogger("TestCategory");
                logger.LogInformation("hello world");
                provider.Flush();
            } // Dispose closes the writer before we read

            var files = Directory.GetFiles(dir, "nhibernaut-*.log");
            Assert.Single(files);

            logContent = File.ReadAllText(files[0]);
            Assert.Contains("hello world", logContent);
            Assert.Contains("INFO", logContent);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
