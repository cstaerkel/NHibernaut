using System;
using Microsoft.Extensions.Logging;

namespace NHibernaut.App.Logging;

internal sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly FileLoggerProvider _provider;
    public FileLogger(string category, FileLoggerProvider provider) { _category = category; _provider = provider; }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => level >= _provider.Options.MinLevel && level != LogLevel.None;

    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {Short(level)} [{_category}] {formatter(state, ex)}";
        if (ex is not null) line += Environment.NewLine + ex;
        _provider.Write(line);
    }

    private static string Short(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRCE", LogLevel.Debug => "DBUG", LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN", LogLevel.Error => "FAIL", LogLevel.Critical => "CRIT", _ => "????"
    };
}
