using System;
using System.Net;
using System.Security.Cryptography;

namespace NHibernaut.Server.Host;

/// <summary>Resolves the deployed dashboard server's bind/port/token from environment variables.</summary>
public sealed class DashboardHostOptions
{
    /// <summary>
    /// Generates a random auth token. Uses hex (URL-safe) so it survives the documented
    /// <c>?token=</c> query-string login — Base64's <c>+</c> would be decoded to a space and reject.
    /// </summary>
    public static string GenerateToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public required string BindAddress { get; init; }
    public required int Port { get; init; }
    public string? AuthToken { get; init; }
    public bool TokenWasGenerated { get; init; }

    /// <param name="getEnv">Environment-variable reader (injected for testability).</param>
    /// <param name="generateToken">Token factory used when a non-loopback bind has no token.</param>
    public static DashboardHostOptions Resolve(Func<string, string?> getEnv, Func<string> generateToken)
    {
        var bind = Blank(getEnv("NHIBERNAUT_BIND")) ?? "0.0.0.0";

        var portText = Blank(getEnv("NHIBERNAUT_PORT")) ?? "5005";
        if (!int.TryParse(portText, out var port) || port is < 1 or > 65535)
            throw new FormatException($"NHIBERNAUT_PORT must be an integer 1..65535, got '{portText}'.");

        var token = Blank(getEnv("NHIBERNAUT_AUTH_TOKEN"));
        var generated = false;
        if (!IsLoopback(bind) && token is null)
        {
            token = generateToken();
            generated = true;
        }

        return new DashboardHostOptions
        {
            BindAddress = bind,
            Port = port,
            AuthToken = token,
            TokenWasGenerated = generated
        };
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    internal static bool IsLoopback(string address)
    {
        if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        var trimmed = address.Trim('[', ']');
        return IPAddress.TryParse(trimmed, out var ip) && IPAddress.IsLoopback(ip);
    }
}
