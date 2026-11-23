using Microsoft.Web.WebView2.Core;
using VJApp.Engine;

namespace VJApp;

/// <summary>
/// O que fazer quando uma parte do programa cai, para o VJesus não fechar no meio do culto:
/// - a placa de vídeo foi reiniciada pelo Windows (driver travou, computador fraco): guarda o que está no telão
///   e abre o VJesus de novo, que recoloca tudo sozinho;
/// - a página da interface travou ou fechou (pouca memória): recarrega só a interface, o telão continua.
/// </summary>
sealed partial class MainForm
{
    bool _restarting;

    /// <summary>
    /// Pedido vindo de outra thread (motor, pastas vigiadas, MIDI, OSC...). Com a janela fechando, o pedido é
    /// ignorado em vez de virar um erro fatal numa thread de fundo (o que fecharia o programa).
    /// </summary>
    public new IAsyncResult? BeginInvoke(Action method)
    {
        try { return IsDisposed || Disposing || !IsHandleCreated ? null : base.BeginInvoke(method); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { return null; }
    }

    void OnRendererLost(Exception ex)
    {
        if (_restarting) return;
        SaveLiveState(force: true);
        if (Program.Relaunch())
        {
            _restarting = true;
            Environment.Exit(0); // sem apagar o "ao vivo": a nova janela recoloca o que estava no telão
        }
        else Send(new { type = "error", message = "A placa de vídeo parou de responder (" + ex.Message + "). Feche e abra o VJesus." });
    }

    int _webReloads;
    DateTime _lastWebReload;

    void OnWebProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Program.LogError("interface", new Exception("WebView2: " + kind));
        if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // o navegador inteiro da interface caiu: só abrindo de novo
            OnRendererLost(new Exception("a interface fechou"));
            return;
        }
        if (kind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
        {
            // recarrega a interface (no máximo algumas vezes seguidas, para não ficar em laço)
            if (DateTime.Now - _lastWebReload > TimeSpan.FromMinutes(2)) _webReloads = 0;
            if (++_webReloads > 3) return;
            _lastWebReload = DateTime.Now;
            try { _web.Reload(); } catch { }
        }
    }
}