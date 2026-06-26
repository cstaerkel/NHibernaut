using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NHibernaut.Mcp.Tests.Integration;

public sealed class McpProcessHarness : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _stderrLines = new();
    private readonly List<string> _stdoutLines = new();
    private readonly Task _stderrPump;
    private int _nextId;

    private McpProcessHarness(Process process)
    {
        _process = process;
        _stderrPump = Task.Run(PumpStderrAsync);
    }

    public IReadOnlyList<string> StdoutLines => _stdoutLines.ToArray();

    public IReadOnlyList<string> StderrLines => _stderrLines.ToArray();

    public static async Task<McpProcessHarness> StartAsync(string dashboardUrl, string? dashboardToken = null)
    {
        var dll = ResolveMcpDllPath();
        var info = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(dll)!,
        };
        info.ArgumentList.Add(dll);
        info.ArgumentList.Add("--url");
        info.ArgumentList.Add(dashboardUrl);
        if (!string.IsNullOrWhiteSpace(dashboardToken))
        {
            info.ArgumentList.Add("--token");
            info.ArgumentList.Add(dashboardToken);
        }

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start nhibernaut-mcp.");
        var harness = new McpProcessHarness(process);
        await harness.WaitForStderrContainsAsync("NHibernaut MCP server", DefaultTimeout).ConfigureAwait(false);
        return harness;
    }

    public async Task InitializeAsync()
    {
        await RequestAsync("initialize", new
        {
            protocolVersion = "2025-11-25",
            capabilities = new { },
            clientInfo = new
            {
                name = "NHibernaut.Mcp.Tests",
                version = "1.0.0",
            },
        }).ConfigureAwait(false);

        await NotifyAsync("notifications/initialized", new { }).ConfigureAwait(false);
    }

    public async Task<JsonElement> ListToolsAsync()
    {
        var result = await RequestAsync("tools/list", new { }).ConfigureAwait(false);
        return result.GetProperty("tools").Clone();
    }

    public async Task<JsonElement> CallToolAsync(string name, object arguments)
        => await RequestAsync("tools/call", new
        {
            name,
            arguments,
        }).ConfigureAwait(false);

    public static string TextContent(JsonElement callToolResult)
    {
        if (!callToolResult.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return callToolResult.GetRawText();
        }

        var parts = content.EnumerateArray()
            .Where(item =>
                item.TryGetProperty("type", out var type) &&
                string.Equals(type.GetString(), "text", StringComparison.Ordinal) &&
                item.TryGetProperty("text", out _))
            .Select(item => item.GetProperty("text").GetString())
            .Where(text => text is not null)
            .ToList();

        return string.Join(Environment.NewLine, parts);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
            }
        }
        catch (InvalidOperationException)
        {
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        try
        {
            await _stderrPump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }

        _process.Dispose();
    }

    private async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _nextId);
        await WriteAsync(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters,
        }).ConfigureAwait(false);

        return await ReadResponseAsync(id).ConfigureAwait(false);
    }

    private async Task NotifyAsync(string method, object parameters)
        => await WriteAsync(new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
        }).ConfigureAwait(false);

    private async Task WriteAsync(object message)
    {
        if (_process.HasExited)
        {
            throw new InvalidOperationException($"nhibernaut-mcp exited with code {_process.ExitCode}: {string.Join(Environment.NewLine, StderrLines)}");
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _process.StandardInput.WriteLineAsync(json).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    private async Task<JsonElement> ReadResponseAsync(int id)
    {
        using var cts = new CancellationTokenSource(DefaultTimeout);
        while (!cts.IsCancellationRequested)
        {
            var line = await _process.StandardOutput.ReadLineAsync(cts.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidOperationException($"nhibernaut-mcp closed stdout before response {id}. Stderr: {string.Join(Environment.NewLine, StderrLines)}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            _stdoutLines.Add(line);
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (responseId.GetInt32() != id)
            {
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException(error.GetRawText());
            }

            return root.GetProperty("result").Clone();
        }

        throw new TimeoutException($"Timed out waiting for JSON-RPC response {id}. Stderr: {string.Join(Environment.NewLine, StderrLines)}");
    }

    private async Task PumpStderrAsync()
    {
        while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            _stderrLines.Enqueue(line);
        }
    }

    private async Task WaitForStderrContainsAsync(string expected, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (StderrLines.Any(line => line.Contains(expected, StringComparison.Ordinal)))
            {
                return;
            }

            if (_process.HasExited)
            {
                throw new InvalidOperationException($"nhibernaut-mcp exited with code {_process.ExitCode}: {string.Join(Environment.NewLine, StderrLines)}");
            }

            await Task.Delay(25, cts.Token).ConfigureAwait(false);
        }
    }

    private static string ResolveMcpDllPath()
    {
        var root = FindRepoRoot();
        var configuration = AppContext.BaseDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("Release", StringComparer.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var dll = Path.Combine(root, "src", "NHibernaut.Mcp", "bin", configuration, "net10.0", "nhibernaut-mcp.dll");
        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"Build NHibernaut.Mcp in {configuration} before running MCP smoke tests.", dll);
        }

        return dll;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NHibernaut.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing NHibernaut.sln.");
    }
}
