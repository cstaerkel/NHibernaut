using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NHibernaut.Client;
using Xunit;

namespace NHibernaut.App.Tests;

public class SseReaderTests
{
    [Fact]
    public async Task Parses_named_events_and_ignores_comments()
    {
        const string stream = ": connected\n\nevent: session\ndata: {\"id\":\"a\"}\n\n: ping\n\nevent: session\ndata: {\"id\":\"b\"}\n\n";
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(stream));

        var events = new List<(string, string)>();
        await foreach (var ev in SseReader.ReadAsync(s))
            events.Add(ev);

        Assert.Equal(new[] { ("session", "{\"id\":\"a\"}"), ("session", "{\"id\":\"b\"}") }, events);
    }
}
