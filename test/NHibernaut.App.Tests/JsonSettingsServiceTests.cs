using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NHibernaut.App.Services;
using NHibernaut.Client;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class JsonSettingsServiceTests
{
    private static ILogger<JsonSettingsService> NullLogger =>
        NullLogger<JsonSettingsService>.Instance;

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaults()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        // Ensure the file does not exist
        if (File.Exists(tempFile)) File.Delete(tempFile);

        try
        {
            var svc = new JsonSettingsService(NullLogger, tempFile);
            await svc.LoadAsync();

            Assert.Equal(DashboardMode.Remote, svc.Current.LastConnection.Mode);
            Assert.Equal("http://127.0.0.1:5005", svc.Current.LastConnection.Url);
            Assert.Equal(LogLevel.Information, svc.Current.LogLevel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SaveAsync_ThenLoad_RoundTripsValues()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        try
        {
            // First instance: load (no file → defaults), mutate, save
            var svc1 = new JsonSettingsService(NullLogger, tempFile);
            await svc1.LoadAsync();

            svc1.Current.LastConnection = DashboardConnection.Embedded("0.0.0.0", 9000);
            svc1.Current.LogLevel = LogLevel.Debug;
            await svc1.SaveAsync();

            // Second instance: load from the persisted file
            var svc2 = new JsonSettingsService(NullLogger, tempFile);
            await svc2.LoadAsync();

            Assert.Equal(DashboardMode.Embedded, svc2.Current.LastConnection.Mode);
            Assert.Equal("0.0.0.0", svc2.Current.LastConnection.BindAddress);
            Assert.Equal(9000, svc2.Current.LastConnection.Port);
            Assert.Equal(LogLevel.Debug, svc2.Current.LogLevel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
