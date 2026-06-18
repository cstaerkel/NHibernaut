using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace NHibernaut.App.Logging;

/// <summary>Thread-safe rolling file logger: one writer thread drains a queue; rolls by size, prunes old files.</summary>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    public FileLoggerOptions Options { get; }
    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly Thread _worker;
    private StreamWriter _writer;
    private string _path;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        Options = options;
        Directory.CreateDirectory(Options.Directory);
        _path = CurrentPath();
        _writer = Open(_path);
        _worker = new Thread(Drain) { IsBackground = true, Name = "nh-filelog" };
        _worker.Start();
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);
    internal void Write(string line)
    {
        try { _queue.Add(line); }
        catch (InvalidOperationException) { /* shutting down: CompleteAdding was called; drop the line */ }
    }
    public void Flush() { Thread.Sleep(50); _writer.Flush(); }

    private void Drain()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                RollIfNeeded();
                _writer.WriteLine(line);
                _writer.Flush();
            }
            catch (Exception ex)
            {
                // Last resort so the loss isn't fully silent; never rethrow (would kill the writer thread).
                try { Console.Error.WriteLine($"[nh-filelog] write error: {ex.Message}"); } catch { }
            }
        }
    }

    private void RollIfNeeded()
    {
        var wanted = CurrentPath();
        if (wanted != _path || _writer.BaseStream.Length >= Options.MaxBytes)
        {
            _writer.Dispose();
            if (wanted == _path) wanted = CurrentPath(roll: true);
            _path = wanted;
            _writer = Open(_path);
            Prune();
        }
    }

    private string CurrentPath(bool roll = false)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd");
        var basePath = Path.Combine(Options.Directory, $"nhibernaut-{stamp}.log");
        if (!roll) return basePath;
        for (var i = 1; ; i++)
        {
            var p = Path.Combine(Options.Directory, $"nhibernaut-{stamp}.{i}.log");
            if (!File.Exists(p)) return p;
        }
    }

    private static StreamWriter Open(string path) =>
        new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8) { AutoFlush = false };

    private void Prune()
    {
        var files = new DirectoryInfo(Options.Directory).GetFiles("nhibernaut-*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(Options.MaxFiles);
        foreach (var f in files) { try { f.Delete(); } catch { /* best effort */ } }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        try { _worker.Join(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
        _writer.Dispose();
    }
}
