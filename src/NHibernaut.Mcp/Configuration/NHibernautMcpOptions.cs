namespace NHibernaut.Mcp.Configuration;

public sealed class NHibernautMcpOptions
{
    public const string DefaultDashboardUrl = "http://127.0.0.1:5005";
    public const int DefaultTimeoutMs = 10000;
    public const int DefaultMaxOutputChars = 25000;

    public string DashboardUrl { get; init; } = DefaultDashboardUrl;
    public string? DashboardToken { get; init; }
    public int TimeoutMs { get; init; } = DefaultTimeoutMs;
    public int MaxOutputChars { get; init; } = DefaultMaxOutputChars;
    public bool AllowSensitiveOutput { get; init; }

    public override string ToString()
        => $"NHibernautMcpOptions {{ DashboardUrl = {DashboardUrl}, DashboardToken={(DashboardToken is null ? "null" : "[redacted]")}, TimeoutMs = {TimeoutMs}, MaxOutputChars = {MaxOutputChars}, AllowSensitiveOutput = {AllowSensitiveOutput} }}";
}
