using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Segurança ao vivo:
/// - Tela de pânico: uma tecla (ou o botão vermelho do topo) cobre o telão na hora com o logo da igreja
///   ou uma cor, por cima de tudo. Apertar de novo volta exatamente para o que estava.
/// - Voltar onde estava: o VJesus anota a cada 2 s o que está no telão; se fechar sem querer (queda de
///   energia, travamento), ao abrir de novo ele coloca de volta os mesmos clips, na mesma posição.
/// </summary>
sealed partial class MainForm
{
    sealed class PanicSettings
    {
        /// <summary>Imagem (logo) no centro; null = só a cor.</summary>
        public string? Image { get; set; }
        public string Color { get; set; } = "#000000";
        /// <summary>Tempo do esmaecer ao entrar e sair (0 = corte seco).</summary>
        public float Fade { get; set; } = 0.4f;
        /// <summary>Tamanho do logo em relação à altura da tela (0.1 a 1).</summary>
        public float Size { get; set; } = 0.5f;
        /// <summary>Fundo: "cor", "video" (um vídeo em repetição) ou "fundo" (fundo animado gerado).</summary>
        public string Background { get; set; } = "cor";
        public string? Video { get; set; }
        public GeneratorSettings? Gen { get; set; }
    }

    /// <summary>O vídeo do fundo (pausado enquanto a tela de pânico está desligada; quem libera é o motor).</summary>
    VideoPlayer? _panicPlayer;
    string? _panicMediaKey;

    bool PanicHasMedia => _panic.Background == "fundo" || (_panic.Background == "video" && _panic.Video is { } v && File.Exists(v));

    /// <summary>Prepara o fundo em movimento no motor (só quando muda o que foi escolhido).</summary>
    void ApplyPanicMedia()
    {
        if (_renderer == null) return;
        _renderer.PanicMediaFade = _panic.Fade;
        string key = !PanicHasMedia ? "" : _panic.Background == "video" ? "v:" + _panic.Video : "g:" + JsonSerializer.Serialize(_panic.Gen);
        if (key == _panicMediaKey) return;
        _panicMediaKey = key;
        _panicPlayer = null;
        if (key == "") { _renderer.SetPanicMedia(null, null, GenParams.Default); return; }
        if (_panic.Background == "fundo") { _renderer.SetPanicMedia(SourceKind.Plasma, null, ToGenParams(_panic.Gen)); return; }
        try
        {
            var player = new VideoPlayer(_panic.Video!);
            if (!_panicOn) player.Pause(); // parado e sem gastar processador até a tela de pânico entrar
            _panicPlayer = player;
            _renderer.SetPanicMedia(SourceKind.Video, player, GenParams.Default);
        }
        catch (Exception ex)
        {
            _renderer.SetPanicMedia(null, null, GenParams.Default);
            Send(new { type = "error", message = "Não consegui abrir o vídeo da tela de pânico: " + ex.Message });
        }
    }

    static string PanicPath => Path.Combine(AppDataDir, "panico.json");
    PanicSettings _panic = LoadPanic();
    readonly LyricsFeed _panicFeed = new();
    bool _panicOn;

    static PanicSettings LoadPanic()
    {
        try { return File.Exists(PanicPath) ? JsonSerializer.Deserialize<PanicSettings>(File.ReadAllText(PanicPath)) ?? new() : new(); }
        catch { return new(); }
    }

    void StartPanic()
    {
        if (_renderer != null) _renderer.Panic = _panicFeed;
        ApplyPanicMedia();
    }

    void OnPanicMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "panicToggle":
                SetPanic(msg["on"] is JsonNode on ? (bool)on : !_panicOn);
                break;
            case "panicSet":
                if (msg["settings"].Deserialize<PanicSettings>(JsonRead) is not { } s) return;
                s.Fade = Math.Clamp(s.Fade, 0, 5);
                s.Size = Math.Clamp(s.Size, 0.1f, 1);
                if (s.Background is not ("cor" or "video" or "fundo")) s.Background = "cor";
                _panic = s;
                try { File.WriteAllText(PanicPath, JsonSerializer.Serialize(_panic)); } catch { }
                ApplyPanicMedia();
                if (_panicOn) _panicFeed.ShowImage(PanicImage(), 0.15f); // atualiza o que está no ar
                SendPanic();
                break;
            case "panicBrowseVideo":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Vídeo da tela de pânico (fica repetindo, sem som)",
                    Filter = "Vídeos|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.wmv;*.m4v|Todos os arquivos|*.*",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _panic.Video = dlg.FileName;
                _panic.Background = "video";
                try { File.WriteAllText(PanicPath, JsonSerializer.Serialize(_panic)); } catch { }
                ApplyPanicMedia();
                if (_panicOn) _panicFeed.ShowImage(PanicImage(), 0.15f);
                SendPanic();
                break;
            }
            case "panicBrowse":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Imagem da tela de pânico (logo da igreja)",
                    Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Todos os arquivos|*.*",
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                _panic.Image = dlg.FileName;
                try { File.WriteAllText(PanicPath, JsonSerializer.Serialize(_panic)); } catch { }
                if (_panicOn) _panicFeed.ShowImage(PanicImage(), 0.15f);
                SendPanic();
                break;
            }
        }
    }

    void SetPanic(bool on)
    {
        if (on == _panicOn) { SendPanic(); return; }
        _panicOn = on;
        if (on) ApplyPanicMedia();
        if (on) _panicPlayer?.Play(); else _panicPlayer?.Pause();
        if (_renderer != null) { _renderer.PanicMediaFade = _panic.Fade; _renderer.PanicMediaOn = on && PanicHasMedia; }
        _panicFeed.ShowImage(on ? PanicImage() : null, _panic.Fade);
        SendPanic();
        SaveLiveState(force: true);
    }

    void SendPanic() => Send(new
    {
        type = "panic",
        on = _panicOn,
        settings = _panic,
        imageName = _panic.Image != null ? Path.GetFileName(_panic.Image) : null,
        imageMissing = _panic.Image != null && !File.Exists(_panic.Image),
        videoName = _panic.Video != null ? Path.GetFileName(_panic.Video) : null,
        videoMissing = _panic.Video != null && !File.Exists(_panic.Video),
        thumb = PanicThumb(),
    });

    /// <summary>A tela de pânico pronta (1920×1080, BGRA): a cor de fundo e o logo no centro.</summary>
    byte[] PanicImage()
    {
        int w = LyricsFeed.Width, h = LyricsFeed.Height;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // com vídeo ou fundo animado, o fundo vem do motor: aqui fica só o logo (fundo transparente)
            g.Clear(PanicHasMedia ? Color.Transparent : ParseColor(_panic.Color));
            if (_panic.Image is { } path && File.Exists(path))
            {
                try
                {
                    using var img = Image.FromFile(path);
                    float box = h * _panic.Size;
                    float scale = Math.Min(box * 1.6f / img.Width, box / img.Height);
                    float iw = img.Width * scale, ih = img.Height * scale;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, (w - iw) / 2, (h - ih) / 2, iw, ih);
                }
                catch { /* imagem com problema: fica só a cor */ }
            }
        }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * w * 4, w * 4);
        bmp.UnlockBits(data);
        return bytes;
    }

    /// <summary>Miniatura do que entra no telão, para o botão de configurar.</summary>
    string? PanicThumb()
    {
        try
        {
            var full = PanicImage();
            int w = LyricsFeed.Width, h = LyricsFeed.Height;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                System.Runtime.InteropServices.Marshal.Copy(full, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
            bmp.UnlockBits(data);
            using var small = new Bitmap(320, 180);
            using (var g = Graphics.FromImage(small)) g.DrawImage(bmp, 0, 0, 320, 180);
            using var ms = new MemoryStream();
            small.Save(ms, ImageFormat.Jpeg);
            return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    static Color ParseColor(string? hex)
    {
        try { return ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(hex) ? "#000000" : hex); }
        catch { return Color.Black; }
    }

    // ---------------- voltar onde estava ----------------

    sealed record LiveLayer(int Layer, string? DeckId, int Column, string? Path, double Position, int Slide);
    /// <summary>O que está no telão: clips, tela de pânico e a letra/Bíblia/texto no ar (para voltar igual depois de uma queda).</summary>
    sealed record LiveState(List<LiveLayer> Layers, bool Panic, DateTime Saved,
        string? SongId = null, int Slide = -1, BiblePassage? Bible = null, string? Text = null, bool Blank = false);

    static string LiveStatePath => Path.Combine(AppDataDir, "ao-vivo.json");
    static string SessionFlagPath => Path.Combine(AppDataDir, "sessao-aberta.txt");
    /// <summary>A sessão anterior não fechou direito (ou o programa foi reaberto depois de cair).</summary>
    bool _uncleanStart;
    bool _recoveryDone;
    DateTime _liveSaved;
    string? _liveLast;

    /// <summary>Chamado ao abrir: percebe se a última vez terminou sem fechar o programa.</summary>
    void BeginSession()
    {
        _uncleanStart = File.Exists(SessionFlagPath) || Environment.GetCommandLineArgs().Contains("--recuperar");
        try { File.WriteAllText(SessionFlagPath, Environment.ProcessId.ToString()); } catch { }
    }

    /// <summary>Fechou pelo X: da próxima vez abre normal, sem recolocar nada.</summary>
    void EndSession()
    {
        try { File.Delete(SessionFlagPath); } catch { }
        try { File.Delete(LiveStatePath); } catch { }
    }

    /// <summary>Anota o que está no telão (a cada 2 s, só quando mudou de clip ou andou o vídeo).</summary>
    void SaveLiveState(bool force = false)
    {
        if (!force && DateTime.Now - _liveSaved < TimeSpan.FromSeconds(2)) return;
        if (!_recoveryDone && _uncleanStart) return; // ainda não recolocou: não apaga o que estava salvo
        _liveSaved = DateTime.Now;
        var layers = new List<LiveLayer>();
        for (int l = 0; l < _comp.Layers.Count; l++)
        {
            var layer = _comp.Layers[l];
            if (layer.PlayingClip is not { } clip) continue;
            double pos = l < _players.Length && _players[l] is { IsLive: false } p ? Math.Round(p.Position, 1) : 0;
            layers.Add(new LiveLayer(l, layer.ActiveDeck, layer.Active, clip.Path, pos, _presSlide.GetValueOrDefault(l)));
        }
        var json = JsonSerializer.Serialize(new LiveState(layers, _panicOn, DateTime.Now, _liveSongId, _liveSlide, _liveBible,
            _liveSongId == null && _liveBible == null ? _liveText : null, _blank));
        if (json == _liveLast) return;
        _liveLast = json;
        try { File.WriteAllText(LiveStatePath, json); } catch { }
    }

    /// <summary>Depois de uma queda: coloca de volta no telão o que estava tocando.</summary>
    void RecoverLiveState()
    {
        if (_recoveryDone) return;
        _recoveryDone = true;
        if (!_uncleanStart || !File.Exists(LiveStatePath)) return;
        LiveState? state;
        try { state = JsonSerializer.Deserialize<LiveState>(File.ReadAllText(LiveStatePath)); }
        catch { return; }
        if (state == null || DateTime.Now - state.Saved > TimeSpan.FromHours(12)) return; // coisa velha: não recoloca
        int restored = 0;
        BeginTriggerGroup();
        foreach (var item in state.Layers)
        {
            if (item.Layer >= _comp.Layers.Count) continue;
            var deck = _comp.Decks.FirstOrDefault(d => d.Id == item.DeckId) ?? _comp.Current;
            if (deck == _comp.Current) _comp.StoreDeck();
            var clip = item.Layer < deck.Clips.Count && item.Column >= 0 && item.Column < deck.Clips[item.Layer].Length
                ? deck.Clips[item.Layer][item.Column] : null;
            if (clip == null || clip.Path != item.Path) continue; // a grade mudou: não arrisca colocar outra coisa
            Trigger(item.Layer, item.Column, clip, deck.Id);
            if (clip.Kind == ClipKind.Presentation && item.Slide > 0) ShowPresentationSlide(item.Layer, clip, item.Slide);
            if (item.Position > 1 && _players[item.Layer] is { } p) p.Seek(item.Position);
            restored++;
        }
        EndTriggerGroup();
        // a letra, o versículo ou o texto que estava no ar
        try
        {
            if (state.SongId != null && state.Slide >= 0) ShowSlide(state.SongId, state.Slide);
            else if (state.Bible != null) _ = ShowBibleAsync(state.Bible);
            else if (state.Text != null) { _liveSongId = null; _liveBible = null; _liveSlide = -1; _liveText = state.Text; _blank = false; UpdateLyricsOutput(); }
            if (state.Blank && (state.SongId != null || state.Bible != null || state.Text != null)) { _blank = true; UpdateLyricsOutput(); }
        }
        catch (Exception ex) { Program.LogError("recuperar letra", ex); }
        if (state.Panic) SetPanic(true);
        SendComposition(withThumbs: false);
        if (restored > 0 || state.Panic)
            Send(new { type = "error", message = $"O VJesus tinha fechado sem querer. Coloquei de volta no telão o que estava tocando ({restored} layer{(restored == 1 ? "" : "s")}{(state.Panic ? ", com a tela de pânico ligada" : "")})." });
    }
}
