using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace NHibernaut.App.Services;

public sealed partial class EditorLinkService : IEditorLinkService
{
    private readonly ILogger<EditorLinkService> _log;
    public EditorLinkService(ILogger<EditorLinkService> log) => _log = log;

    // Matches the custom stack-trace format emitted by CaptureContext:
    //   TypeName.MethodName (C:\path\to\File.cs:42)
    // The greedy .+ backtracks past drive-letter colons to the last :digits before ).
    [GeneratedRegex(@"\((?<file>.+):(?<line>\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex FrameRegex();

    public bool TryBuildLink(string? stackTrace, string scheme, out string? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(stackTrace)) return false;
        var m = FrameRegex().Match(stackTrace);
        if (!m.Success) return false;
        var file = m.Groups["file"].Value.Trim();
        var line = m.Groups["line"].Value;
        link = $"{scheme}://file/{file}:{line}";
        return true;
    }

    public void Open(string link)
    {
        try { Process.Start(new ProcessStartInfo(link) { UseShellExecute = true }); }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to open editor link {Link}", link); }
    }
}
