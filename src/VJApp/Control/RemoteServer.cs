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

        string page = Path.Combine(AppContext.BaseDirectory, "Remote", "index.html");
        app.UseWebSockets();
        app.MapGet("/", () => Results.File(page, "text/html; charset=utf-8"));
        app.Map("/ws", server.HandleSocket);
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
