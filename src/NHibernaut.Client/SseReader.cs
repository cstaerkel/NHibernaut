using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace NHibernaut.Client;

/// <summary>Minimal text/event-stream parser. Yields (eventName, data) for frames that carry data.</summary>
public static class SseReader
{
    public static async IAsyncEnumerable<(string Event, string Data)> ReadAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string evt = "message";
        var data = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;                         // stream closed

            if (line.Length == 0)                            // dispatch on blank line
            {
                if (data.Length > 0)
                {
                    yield return (evt, data.ToString());
                    data.Clear();
                }
                evt = "message";
                continue;
            }
            if (line[0] == ':') continue;                    // comment / heartbeat
            if (line.StartsWith("event:")) evt = line[6..].Trim();
            else if (line.StartsWith("data:")) data.Append(line[5..].TrimStart());
        }
    }
}
