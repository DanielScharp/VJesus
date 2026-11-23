using Microsoft.Web.WebView2.Core;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// O que fazer quando uma parte do programa cai, para o VJesus não fechar no meio do culto e o telão não sumir:
/// - a placa de vídeo foi reiniciada pelo Windows (driver travou, computador fraco): o motor de vídeo é
///   recriado por dentro, sem fechar o programa nem as janelas do telão, e o que estava tocando volta no lugar
///   (o telão fica parado na última imagem por um instante, em vez de apagar);
/// - a página da interface travou ou fechou (pouca memória): recarrega só a interface, o telão continua.
/// </summary>
sealed partial class MainForm
{
    /// <summary>
    /// Pedido vindo de outra thread (motor, pastas vigiadas, MIDI, OSC...). Com a janela fechando, o pedido é
    /// ignorado em vez de virar um erro fatal numa thread de fundo (o que fecharia o programa).
    /// </summary>
    public new IAsyncResult? BeginInvoke(Action method)
    {
        try { return IsDisposed || Disposing || !IsHandleCreated ? null : base.BeginInvoke(method); }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { return null; }
    }

    /// <summary>Cria o motor de vídeo e passa para ele a composição, os efeitos e o mapeamento.</summary>
    void CreateRenderer(float bpm)
    {
        var r = new Renderer(_comp.Layers.Count, _effectDefs);
        _renderer = r;
        r.Stats += fps => Send(new { type = "stats", fps = Math.Round(fps, 1), cpu = Math.Round(_sysMon.Cpu), ram = Math.Round(_sysMon.Ram), vram = Math.Round(_sysMon.Vram), vramInfo = _sysMon.VramInfo });
        r.Preview += OnPreview;
        r.StagePreview += OnStagePreview;
        r.OutputPreview += OnOutputPreview;
        r.Error += ex => Send(new { type = "error", message = "Motor de vídeo: " + ex.Message });
        r.EffectError += msg => Send(new { type = "error", message = msg });
        // só reage se ainda for o motor atual (um antigo pode avisar atrasado)
        r.DeviceLost += ex => BeginInvoke(() => { if (_renderer == r) RestartRenderer(ex); });
        for (int i = 0; i < _comp.Layers.Count; i++) ApplyLayerParams(i);
        r.SetBpm(bpm);
        RebuildEffectChains();
        ApplyMapping();
    }

    int _engineRestarts;
    DateTime _engineRestartWindow;

    /// <summary>
    /// Recria o motor de vídeo sem fechar o programa: guarda o que está no telão, libera o motor que parou,
    /// cria outro nas mesmas janelas e recoloca os mesmos clips na mesma posição.
    /// </summary>
    void RestartRenderer(Exception cause)
    {
        if (_renderer is not { } old) return;
        if (DateTime.Now - _engineRestartWindow > TimeSpan.FromMinutes(5)) { _engineRestarts = 0; _engineRestartWindow = DateTime.Now; }
        if (++_engineRestarts > 5)
        {
            Send(new { type = "error", message = "A placa de vídeo está falhando muitas vezes seguidas (" + cause.Message + "). Salve o que precisar e reinicie o computador." });
            return;
        }

        // 1) o que está no telão agora
        var playing = new List<LiveLayer>();
        for (int l = 0; l < _comp.Layers.Count; l++)
        {
            var layer = _comp.Layers[l];
            if (layer.PlayingClip is not { } clip) continue;
            double pos = l < _players.Length && _players[l] is { IsLive: false } p ? p.Position : 0;
            playing.Add(new LiveLayer(l, layer.ActiveDeck, layer.Active, clip.Path, pos, _presSlide.GetValueOrDefault(l)));
        }
        float brightness = old.Brightness, speed = old.Speed, bpm = old.Bpm;
        bool testCard = old.ShowTestCard;

        // 2) solta o motor que parou (as janelas do telão continuam abertas)
        _renderer = null;
        try { _audio?.Dispose(); } catch { }
        _audio = null;
        try { old.Dispose(); } catch (Exception ex) { Program.LogError("motor (liberar)", ex); }
        for (int i = 0; i < _players.Length; i++) _players[i] = null; // eram do motor antigo (já liberados)
        _webPages.Clear();
        foreach (var layer in _comp.Layers) { layer.PlayingClip = null; layer.Active = -1; }

        // 3) motor novo, com tudo o que o antigo tinha
        try
        {
            CreateRenderer(bpm);
            var r = _renderer!;
            r.Brightness = brightness;
            r.Speed = speed;
            r.ShowTestCard = testCard;
            r.StageMirror = _stageMirror;
            r.Overlay = _overlayFeed;
            ApplyOverlayTargets();
            r.Panic = _panicFeed;
            _panicMediaKey = null;
            ApplyPanicMedia();
            r.PanicMediaOn = _panicOn && PanicHasMedia;
            if (_panicOn) _panicPlayer?.Play();
            r.StreamState += (spout, ndi) => BeginInvoke(() => { _streamActive = (spout, ndi); SendStreaming(); });
            r.StreamError += (what, msg) => BeginInvoke(() =>
            {
                if (what == "NDI") _stream.Ndi = null; else _stream.Spout = null;
                SaveStreamSettings();
                Send(new { type = "error", message = $"{what}: {msg}" });
            });
            r.SetStreaming(_stream.Spout, _stream.Ndi);
            ApplyStageWindow();
            StartAudio();
        }
        catch (Exception ex)
        {
            Program.LogError("motor (recriar)", ex);
            Send(new { type = "error", message = "Não consegui religar o motor de vídeo: " + ex.Message + ". Feche e abra o VJesus." });
            return;
        }

        // 4) recoloca no telão o que estava tocando
        BeginTriggerGroup();
        foreach (var item in playing)
        {
            if (item.Layer >= _comp.Layers.Count) continue;
            var deck = _comp.Decks.FirstOrDefault(d => d.Id == item.DeckId) ?? _comp.Current;
            if (deck == _comp.Current) _comp.StoreDeck();
            var clip = item.Layer < deck.Clips.Count && item.Column >= 0 && item.Column < deck.Clips[item.Layer].Length
                ? deck.Clips[item.Layer][item.Column] : null;
            if (clip == null) continue;
            try
            {
                Trigger(item.Layer, item.Column, clip, deck.Id);
                if (clip.Kind == ClipKind.Presentation && item.Slide > 0) ShowPresentationSlide(item.Layer, clip, item.Slide);
                if (item.Position > 0.5 && _players[item.Layer] is { } p) p.Seek(item.Position);
            }
            catch (Exception ex) { Program.LogError("motor (recolocar)", ex); }
        }
        EndTriggerGroup();
        SendComposition(withThumbs: false);
        Send(new { type = "error", message = "A placa de vídeo parou por um instante. O VJesus religou o motor e recolocou o telão." });
    }

    int _webReloads;
    DateTime _lastWebReload;

    void OnWebProcessFailed(CoreWebView2ProcessFailedKind kind)
    {
        Program.LogError("interface", new Exception("WebView2: " + kind));
        if (kind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // a interface inteira caiu; o telão é desenhado à parte e continua. Quem decide a hora de reabrir é o operador.
            var r = MessageBox.Show(this,
                "A tela de controle do VJesus parou, mas o telão continua normal.\n\n" +
                "Clique em OK quando puder para reabrir o VJesus (o telão volta em poucos segundos com o que estava tocando).",
                AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.OK)
            {
                SaveLiveState(force: true);
                if (Program.Relaunch()) Environment.Exit(0);
            }
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
