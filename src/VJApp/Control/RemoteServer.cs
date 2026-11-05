using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace VJApp.Control;

/// <summary>
/// Controle remoto pelo celular/tablet: servidor web na rede local (porta 8080) com uma página
/// (Remote/index.html) que conversa com o app por WebSocket em /ws.
/// </summary>
sealed class RemoteServer : IAsyncDisposable
{
    readonly WebApplication _app;
    readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> _clients = new();
    readonly Action<string> _onMessage;
    readonly Func<string> _snapshot;

    public int Port { get; }
    public string Url => $"http://{LocalIp()}:{Port}/";

    RemoteServer(WebApplication app, int port, Action<string> onMessage, Func<string> snapshot)
    {
        _app = app;
        Port = port;
        _onMessage = onMessage;
        _snapshot = snapshot;
    }

    /// <param name="onMessage">JSON recebido de um controle remoto.</param>
    /// <param name="snapshot">JSON com o estado atual, mandado quando um remoto conecta.</param>
    public static async Task<RemoteServer> StartAsync(int port, Action<string> onMessage, Func<string> snapshot)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var server = new RemoteServer(app, port, onMessage, snapshot);

        string Page(string name) => Path.Combine(AppContext.BaseDirectory, "Remote", name);
        app.UseWebSockets();
        app.MapGet("/", () => Results.File(Page("index.html"), "text/html; charset=utf-8"));
        app.Map("/ws", server.HandleSocket);
        // outros computadores da rede: o telão ao vivo, o roteiro e a letra (para o OBS)
        app.MapGet("/telao", () => Results.File(Page("telao.html"), "text/html; charset=utf-8"));
        app.MapGet("/roteiro", () => Results.File(Page("roteiro.html"), "text/html; charset=utf-8"));
        app.MapGet("/letra", () => Results.File(Page("letra.html"), "text/html; charset=utf-8"));
        app.MapGet("/telao.mjpg", server.HandleStream);
        app.MapGet("/eventos", server.HandleEvents);
        await app.StartAsync();
        return server;
    }

    async Task HandleSocket(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
        var gate = new SemaphoreSlim(1, 1);
        _clients[ws] = gate;
        await SendTo(ws, gate, _snapshot());

        var buffer = new byte[16 * 1024];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                var r = await ws.ReceiveAsync(buffer, ctx.RequestAborted);
                if (r.MessageType == WebSocketMessageType.Close) break;
                if (r.EndOfMessage) _onMessage(Encoding.UTF8.GetString(buffer, 0, r.Count));
            }
        }
        catch { /* celular desconectou */ }
        finally
        {
            _clients.TryRemove(ws, out _);
        }
    }

    /// <summary>Manda o JSON para todos os remotos conectados.</summary>
    public void Broadcast(string json)
    {
        foreach (var (ws, gate) in _clients) _ = SendTo(ws, gate, json);
    }

    static async Task SendTo(WebSocket ws, SemaphoreSlim gate, string json)
    {
        await gate.WaitAsync();
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch { }
        finally { gate.Release(); }
    }

    // ---------------- telão ao vivo (MJPEG: funciona em qualquer navegador e no OBS) ----------------

    byte[]? _frame;
    int _frameVersion, _viewers;

    /// <summary>Alguém está vendo o telão pela rede (só então o app gera as imagens para isso).</summary>
    public bool WantsFrames => Volatile.Read(ref _viewers) > 0;

    public void PublishFrame(byte[] jpeg)
    {
        Volatile.Write(ref _frame, jpeg);
        Interlocked.Increment(ref _frameVersion);
    }

    async Task HandleStream(HttpContext ctx)
    {
        const string boundary = "quadro";
        ctx.Response.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";
        ctx.Response.Headers.CacheControl = "no-cache, no-store";
        Interlocked.Increment(ref _viewers);
        int sent = -1;
        try
        {
            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                int v = Volatile.Read(ref _frameVersion);
                if (v != sent && Volatile.Read(ref _frame) is { } jpeg)
                {
                    sent = v;
                    var head = Encoding.ASCII.GetBytes($"--{boundary}\r\nContent-Type: image/jpeg\r\nContent-Length: {jpeg.Length}\r\n\r\n");
                    await ctx.Response.Body.WriteAsync(head, ctx.RequestAborted);
                    await ctx.Response.Body.WriteAsync(jpeg, ctx.RequestAborted);
                    await ctx.Response.Body.WriteAsync("\r\n"u8.ToArray(), ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
                await Task.Delay(40, ctx.RequestAborted);
            }
        }
        catch { /* o outro computador fechou a página */ }
        finally { Interlocked.Decrement(ref _viewers); }
    }

    // ---------------- estado ao vivo (roteiro e letra), por Server-Sent Events ----------------

    string _live = "{}";
    readonly ConcurrentDictionary<HttpResponse, SemaphoreSlim> _listeners = new();

    /// <summary>Alguém está com o roteiro ou a letra aberta pela rede.</summary>
    public bool HasListeners => !_listeners.IsEmpty;

    public void PublishLive(string json)
    {
        if (json == _live) return;
        _live = json;
        foreach (var (res, gate) in _listeners) _ = WriteEvent(res, gate, json);
    }

    static async Task WriteEvent(HttpResponse res, SemaphoreSlim gate, string json)
    {
        await gate.WaitAsync();
        try
        {
            await res.WriteAsync($"data: {json}\n\n");
            await res.Body.FlushAsync();
        }
        catch { }
        finally { gate.Release(); }
    }

    async Task HandleEvents(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/event-stream; charset=utf-8";
        ctx.Response.Headers.CacheControl = "no-cache";
        var gate = new SemaphoreSlim(1, 1);
        _listeners[ctx.Response] = gate;
        try
        {
            await WriteEvent(ctx.Response, gate, _live);
            // mantém a conexão viva (alguns roteadores derrubam conexões paradas)
            while (!ctx.RequestAborted.IsCancellationRequested)
            {
                await Task.Delay(15000, ctx.RequestAborted);
                await gate.WaitAsync();
                try { await ctx.Response.WriteAsync(": ping\n\n"); await ctx.Response.Body.FlushAsync(); }
                finally { gate.Release(); }
            }
        }
        catch { }
        finally { _listeners.TryRemove(ctx.Response, out _); }
    }

    /// <summary>IP da máquina na rede local (para mostrar o endereço do remoto).</summary>
    static string LocalIp()
    {
        var ip = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        return ip?.ToString() ?? IPAddress.Loopback.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
