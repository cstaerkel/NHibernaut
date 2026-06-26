using System;
using System.Net;
using System.Net.Http;

namespace NHibernaut.Mcp.Tools;

public static class ToolErrorMapper
{
    public static string InvalidGuid(string parameterName, string? value)
        => $"Invalid {parameterName}: expected a GUID like 00000000-0000-0000-0000-000000000000.";

    public static string Map(Exception exception, string dashboardUrl)
    {
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
        {
            return "The NHibernaut dashboard requires a token. Set NHIBERNAUT_DASHBOARD_TOKEN or pass --token, then retry.";
        }

        if (exception is HttpRequestException)
        {
            return $"Could not connect to the NHibernaut dashboard at {dashboardUrl}. Start NHibernautServer, the standalone service, or the desktop embedded collector, then retry.";
        }

        return "The NHibernaut MCP tool hit an unexpected error. Check stderr logs for diagnostics and retry with narrower inputs.";
    }
}
