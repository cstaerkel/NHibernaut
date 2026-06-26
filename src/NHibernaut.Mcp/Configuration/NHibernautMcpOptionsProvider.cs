using System;
using System.Collections.Generic;

namespace NHibernaut.Mcp.Configuration;

public static class NHibernautMcpOptionsProvider
{
    public static NHibernautMcpOptions Resolve(string[] args)
        => Resolve(args, Environment.GetEnvironmentVariable);

    public static NHibernautMcpOptions Resolve(string[] args, Func<string, string?> environment)
    {
        if (args is null) throw new ArgumentNullException(nameof(args));
        if (environment is null) throw new ArgumentNullException(nameof(environment));

        var cli = ParseArgs(args);
        return new NHibernautMcpOptions
        {
            DashboardUrl = FirstNonEmpty(
                Get(cli, "url"),
                environment("NHIBERNAUT_DASHBOARD_URL"),
                NHibernautMcpOptions.DefaultDashboardUrl)!,
            DashboardToken = FirstNonEmpty(
                Get(cli, "token"),
                environment("NHIBERNAUT_DASHBOARD_TOKEN"),
                environment("NHIBERNAUT_AUTH_TOKEN")),
            TimeoutMs = PositiveIntOrDefault(
                FirstNonEmpty(Get(cli, "timeout-ms"), environment("NHIBERNAUT_MCP_TIMEOUT_MS")),
                NHibernautMcpOptions.DefaultTimeoutMs),
            MaxOutputChars = PositiveIntOrDefault(
                FirstNonEmpty(Get(cli, "max-output-chars"), environment("NHIBERNAUT_MCP_MAX_OUTPUT_CHARS")),
                NHibernautMcpOptions.DefaultMaxOutputChars),
            AllowSensitiveOutput = string.Equals(
                environment("NHIBERNAUT_MCP_ALLOW_SENSITIVE"),
                "1",
                StringComparison.Ordinal),
        };
    }

    private static IReadOnlyDictionary<string, string?> ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;

            var keyValue = arg.Substring(2);
            var equals = keyValue.IndexOf('=');
            if (equals >= 0)
            {
                values[keyValue.Substring(0, equals)] = keyValue.Substring(equals + 1);
                continue;
            }

            values[keyValue] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : null;
        }

        return values;
    }

    private static string? Get(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        return null;
    }

    private static int PositiveIntOrDefault(string? value, int fallback)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
