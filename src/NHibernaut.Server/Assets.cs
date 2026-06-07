using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;

namespace NHibernaut.Server;

/// <summary>
/// Serves the embedded dashboard SPA from the assembly's manifest resources (wwwroot/**). Resources
/// are matched by suffix to stay robust against MSBuild's logical-name transforms.
/// </summary>
public static class Assets
{
    private static readonly Assembly Asm = typeof(Assets).Assembly;
    private static readonly string[] ResourceNames = Asm.GetManifestResourceNames();

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".png"] = "image/png",
        [".map"] = "application/json; charset=utf-8"
    };

    public static void Serve(HttpListenerContext context, string path)
    {
        if (TryGet(path, out var bytes, out var contentType))
            Write(context, 200, contentType, bytes);
        else
            Write(context, 404, "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes("Not found"));
    }

    /// <summary>Reads an embedded dashboard asset by request path. Used by both transports.</summary>
    public static bool TryGet(string path, out byte[] content, out string contentType)
    {
        var relative = path is "/" or "" ? "index.html" : path.TrimStart('/');
        // resource names look like "NHibernaut.Server.wwwroot.app.js"
        var suffix = ".wwwroot." + relative.Replace('/', '.');

        var resource = ResourceNames.FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
        if (resource is null)
        {
            content = Array.Empty<byte>();
            contentType = "text/plain; charset=utf-8";
            return false;
        }

        using var stream = Asm.GetManifestResourceStream(resource)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        content = memory.ToArray();

        var ext = Path.GetExtension(relative);
        contentType = ContentTypes.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        return true;
    }

    private static void Write(HttpListenerContext context, int status, string contentType, byte[] body)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = body.Length;
        context.Response.OutputStream.Write(body, 0, body.Length);
        context.Response.Close();
    }
}
