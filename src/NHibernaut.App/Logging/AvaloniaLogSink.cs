using System;
using Avalonia.Logging;
using Microsoft.Extensions.Logging;
using MelLevel = Microsoft.Extensions.Logging.LogLevel;

namespace NHibernaut.App.Logging;

/// <summary>
/// Bridges Avalonia's internal logging into Microsoft.Extensions.Logging.
/// Implements the Avalonia 12.0.4 ILogSink interface:
///   bool IsEnabled(LogEventLevel level, string area)
///   void Log(LogEventLevel level, string area, object? source, string messageTemplate)
///   void Log(LogEventLevel level, string area, object? source, string messageTemplate, object[] propertyValues)
/// </summary>
public sealed class AvaloniaLogSink : ILogSink
{
    private readonly ILogger _logger;

    public AvaloniaLogSink(ILogger logger) => _logger = logger;

    public bool IsEnabled(LogEventLevel level, string area) => _logger.IsEnabled(Map(level));

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Log(level, area, source, messageTemplate, Array.Empty<object?>());

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area)) return;
        var values = new object?[propertyValues.Length + 1];
        values[0] = area;
        Array.Copy(propertyValues, 0, values, 1, propertyValues.Length);
        _logger.Log(Map(level), "[{Area}] " + messageTemplate, values);
    }

    private static MelLevel Map(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose     => MelLevel.Trace,
        LogEventLevel.Debug       => MelLevel.Debug,
        LogEventLevel.Information => MelLevel.Information,
        LogEventLevel.Warning     => MelLevel.Warning,
        LogEventLevel.Error       => MelLevel.Error,
        LogEventLevel.Fatal       => MelLevel.Critical,
        _                         => MelLevel.Debug,
    };
}
