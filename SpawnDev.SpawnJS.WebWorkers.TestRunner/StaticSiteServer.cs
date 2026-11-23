using System.Net;

/// <summary>
/// A minimal static file server (HttpListener on localhost) for a PUBLISHED wwwroot, used by --published. Serves files with the
/// content types a Blazor WebAssembly app needs (application/wasm for streaming instantiation), and index.html for "/" and for
/// paths without a file (client-side routes).
/// </summary>
sealed class StaticSiteServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly string _root;
    readonly CancellationTokenSource _stop = new();
    public string Url { get; }

    static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript",
        [".mjs"] = "text/javascript",
        [".json"] = "application/json",
        [".css"] = "text/css",
        [".wasm"] = "application/wasm",
        [".png"] = "image/png",
        [".ico"] = "image/x-icon",
        [".svg"] = "image/svg+xml",
    };

    public StaticSiteServer(string root)
    {
        _root = Path.GetFullPath(root);
        // a free port: bind 0 is not possible with HttpListener, so probe from a base
        int port = 5300 + Random.Shared.Next(0, 600);
        Url = $"http://localhost:{port}/";
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    void Handle(HttpListenerContext ctx)
    {
        try
        {
            var rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
            var path = Path.GetFullPath(Path.Combine(_root, rel));
            if (!path.StartsWith(_root, StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 403; return; }
            if (Directory.Exists(path)) path = Path.Combine(path, "index.html");
            if (!File.Exists(path))
            {
                // a client-side route (no extension) gets the app; a missing file is a 404
                if (Path.HasExtension(rel)) { ctx.Response.StatusCode = 404; return; }
                path = Path.Combine(_root, "index.html");
            }
            ctx.Response.ContentType = ContentTypes.TryGetValue(Path.GetExtension(path), out var ct) ? ct : "application/octet-stream";
            var bytes = File.ReadAllBytes(path);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; } catch { }
        }
        finally
        {
            try { ctx.Response.Close(); } catch { }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
    }
}
