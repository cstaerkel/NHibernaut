using Microsoft.Extensions.Logging.Abstractions;
using NHibernaut.App.Services;
using Xunit;

namespace NHibernaut.App.Tests;

public sealed class EditorLinkServiceTests
{
    private static EditorLinkService Create() =>
        new(NullLogger<EditorLinkService>.Instance);

    [Fact]
    public void TryBuildLink_extracts_first_frame()
    {
        // The real format emitted by CaptureContext.cs:
        //   TypeName.MethodName (C:\path\to\File.cs:42)
        // (NOT the standard .NET "in file:line N" format)
        var trace = "App.Handlers.Foo (C:\\src\\Handlers.cs:42)\nApp.Handlers.Bar (C:\\src\\Handlers.cs:10)";
        var svc = Create();

        var result = svc.TryBuildLink(trace, "vscode", out var link);

        Assert.True(result);
        Assert.Equal("vscode://file/C:\\src\\Handlers.cs:42", link);
    }

    [Fact]
    public void TryBuildLink_returns_false_for_null()
    {
        var svc = Create();
        var result = svc.TryBuildLink(null, "vscode", out var link);
        Assert.False(result);
        Assert.Null(link);
    }

    [Fact]
    public void TryBuildLink_returns_false_for_empty()
    {
        var svc = Create();
        var result = svc.TryBuildLink("   ", "vscode", out var link);
        Assert.False(result);
        Assert.Null(link);
    }

    [Fact]
    public void TryBuildLink_returns_false_when_no_matching_frame()
    {
        var svc = Create();
        var result = svc.TryBuildLink("App.Handlers.Foo\nSome other text", "vscode", out var link);
        Assert.False(result);
        Assert.Null(link);
    }
}
