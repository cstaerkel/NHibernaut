using Microsoft.Extensions.Logging;

namespace NHibernaut.App.Logging;

public sealed class FileLoggerOptions
{
    public string Directory { get; set; } = AppPaths.LogsDirectory;
    public LogLevel MinLevel { get; set; } = LogLevel.Information;
    public long MaxBytes { get; set; } = 10 * 1024 * 1024;   // roll at 10 MB
    public int MaxFiles { get; set; } = 7;
}
