using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NHibernaut.App.Logging;

namespace NHibernaut.App.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILogger<JsonSettingsService> _logger;
    private readonly string _filePath;

    public AppSettings Current { get; private set; } = new();

    /// <summary>
    /// Production constructor used by DI. The <paramref name="settingsFilePath"/> is optional;
    /// when omitted (or null), it defaults to <see cref="AppPaths.SettingsFile"/> so production
    /// behaviour is unchanged. Passing an explicit path enables testing without touching the real
    /// user directory (option b from the M5 spec).
    /// </summary>
    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? settingsFilePath = null)
    {
        _logger = logger;
        _filePath = settingsFilePath ?? AppPaths.SettingsFile;
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions);
            Current = loaded ?? new AppSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings from {Path}; using defaults", _filePath);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        await using var stream = File.Open(_filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions);
    }
}
