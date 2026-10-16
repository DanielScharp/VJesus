using System.Drawing.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Aba Letras: biblioteca de músicas, projeção de slides e estilo do texto.</summary>
sealed partial class MainForm
{
    readonly LyricsFeed _lyricsFeed = new();
    LyricsLibrary _lyrics = LyricsLibrary.LoadOrDefault(LyricsLibrary.DefaultPath);

    // o que está no telão agora
    string? _liveSongId;
    int _liveSlide = -1;
    string? _liveText;     // texto do slide (ou texto rápido)
    bool _blank;

    void OnLyricsMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "lyricsSave":
            {
                var song = msg["song"].Deserialize<Song>(JsonRead);
                if (song == null) return;
                song.Title = string.IsNullOrWhiteSpace(song.Title) ? "Sem título" : song.Title.Trim();
                song.UpdatedAt = DateTime.Now;
                int i = _lyrics.Songs.FindIndex(s => s.Id == song.Id);
                if (i >= 0) _lyrics.Songs[i] = song; else _lyrics.Songs.Add(song);
                LyricsChanged();
                // se é a música no telão, reprojeta o slide atual com o texto novo
                if (_liveSongId == song.Id && _liveSlide >= 0) ShowSlide(song.Id, _liveSlide);
                break;
            }
            case "lyricsDelete":
                _lyrics.Songs.RemoveAll(s => s.Id == (string?)msg["id"]);
                LyricsChanged();
                break;
            case "lyricsImport":
                ImportSongs();
                break;
            case "lyricsShow":
                ShowSlide((string)msg["songId"]!, (int)msg["slide"]!);
                break;
            case "lyricsText":
                _liveSongId = null;
                _liveBible = null;
                _liveSlide = -1;
                _liveText = (string?)msg["text"];
                _blank = false;
                UpdateLyricsOutput();
                break;
            case "lyricsClear":
                ClearLyrics();
                break;
            case "lyricsBlank":
                _blank = (bool?)msg["blank"] ?? !_blank;
                UpdateLyricsOutput();
                break;
            case "lyricsStep":
                StepSlide((int)msg["direction"]!);
                break;
            case "lyricsStyle":
                // muda o estilo padrão ou o de uma cena
                if (msg["style"].Deserialize<LyricStyle>(JsonRead) is { } style)
                {
                    if ((string?)msg["sceneId"] is { } sceneId && _lyrics.Scenes.FirstOrDefault(s => s.Id == sceneId) is { } scene)
                        scene.Style = style;
                    else
                        _lyrics.Style = style;
                    LyricsChanged(sendLibrary: false);
                    UpdateLyricsOutput(quiet: true);
                }
                break;
            case "lyricsSceneSave":
                if (msg["scene"].Deserialize<LyricScene>(JsonRead) is { } sc)
                {
                    int i = _lyrics.Scenes.FindIndex(s => s.Id == sc.Id);
                    if (i >= 0) _lyrics.Scenes[i] = sc; else _lyrics.Scenes.Add(sc);
                    LyricsChanged();
                    UpdateLyricsOutput(quiet: true);
                }
                break;
            case "lyricsSceneDelete":
            {
                string? id = (string?)msg["id"];
                _lyrics.Scenes.RemoveAll(s => s.Id == id);
                foreach (var s in _lyrics.Songs.Where(s => s.SceneId == id)) s.SceneId = null;
                LyricsChanged();
                UpdateLyricsOutput(quiet: true);
                break;
            }
            case "lyricsToGrid":
            {
                // coloca o clip "Letras" na célula pedida e já toca
                int l = (int)msg["layer"]!, c = (int)msg["column"]!;
                _comp.Layers[l].Clips[c] = new Clip { Kind = ClipKind.Lyrics, Name = "Letras" };
                Trigger(l, c);
                CompositionChanged();
                break;
            }
        }
    }

    void ShowSlide(string songId, int slide)
    {
        var song = _lyrics.Songs.FirstOrDefault(s => s.Id == songId);
        if (song == null) return;
        var slides = LyricsLibrary.Slides(song.Text);
        if (slides.Count == 0) return;
        _liveSongId = songId;
        _liveBible = null;
        _liveSlide = Math.Clamp(slide, 0, slides.Count - 1);
        _liveText = slides[_liveSlide].Text;
        _blank = false;
        UpdateLyricsOutput();
    }

    /// <summary>Fim da música: tira a letra do telão e da tela de retorno (atual e próximo).</summary>
    void ClearLyrics()
    {
        bool wasBible = _liveBible != null;
        _liveBible = null;
        _liveSongId = null;
        _liveSlide = -1;
        _liveText = null;
        _blank = false;
        UpdateLyricsOutput(quiet: true);
        if (wasBible) SendBibleState();
    }

    /// <summary>Próximo/anterior slide da música no telão (teclado, OSC, celular).</summary>
    void StepSlide(int direction)
    {
        if (_liveBible != null) { StepBible(direction); return; }
        if (_liveSongId == null) return;
        ShowSlide(_liveSongId, _liveSlide + direction);
    }

    /// <summary>Estilo do que está no telão: cena da música, cena e ajustes do slide.</summary>
    LyricStyle LiveStyle()
    {
        if (_liveBible != null) return BibleStyle();
        if (_liveSongId == null) return _lyrics.Style;
        var song = _lyrics.Songs.FirstOrDefault(s => s.Id == _liveSongId);
        var slides = song != null ? LyricsLibrary.Slides(song.Text) : [];
        var slide = _liveSlide >= 0 && _liveSlide < slides.Count ? slides[_liveSlide] : null;
        return _lyrics.ResolveStyle(song, slide);
    }

    void UpdateLyricsOutput(bool quiet = false)
    {
        _lyricsFeed.Show(_blank ? null : _liveText, LiveStyle());
        SendLyricsState();
        if (!quiet && !_comp.Layers.Any(l => l.Clips.Any(c => c?.Kind == ClipKind.Lyrics)) && !_blank && _liveText != null)
            Send(new { type = "notice", message = "Para a letra aparecer no telão, coloque o clip Letras na grade (botão \"Colocar na grade\")." });
    }

    void ImportSongs()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Letras em texto|*.txt|Todos os arquivos|*.*",
            Title = "Importar letras (.txt: um arquivo por música)",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int count = 0;
        foreach (var file in dlg.FileNames)
        {
            try
            {
                string text = File.ReadAllText(file, Encoding.UTF8);
                _lyrics.Songs.Add(new Song { Title = Path.GetFileNameWithoutExtension(file), Text = text.Trim() });
                count++;
            }
            catch (Exception ex)
            {
                Send(new { type = "error", message = $"Não consegui importar {Path.GetFileName(file)}: {ex.Message}" });
            }
        }
        LyricsChanged();
        Send(new { type = "notice", message = $"{count} música(s) importada(s)" });
    }

    void LyricsChanged(bool sendLibrary = true)
    {
        _lyrics.Save(LyricsLibrary.DefaultPath);
        if (sendLibrary) SendLyricsLibrary();
    }

    void SendLyricsLibrary() => Send(new
    {
        type = "lyricsLibrary",
        songs = _lyrics.Songs.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase),
        style = _lyrics.Style,
        scenes = _lyrics.Scenes,
    });

    void SendLyricsState()
    {
        SendStage();
        SendLyricsStateOnly();
    }

    void SendLyricsStateOnly() => Send(new
    {
        type = "lyricsState",
        songId = _liveSongId,
        slide = _liveSlide,
        text = _liveText,
        blank = _blank,
    });

    void SendFonts()
    {
        using var fonts = new InstalledFontCollection();
        var imported = FontStore.Families.Distinct().OrderBy(f => f).ToArray();
        Send(new
        {
            type = "fonts",
            list = imported.Concat(fonts.Families.Select(f => f.Name)).Distinct().ToArray(),
            imported,
        });
    }

    /// <summary>Fontes próprias do VJApp: importar arquivos, baixar do Google Fonts, apagar.</summary>
    void OnFontMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "fontsImport":
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Importar fontes (.ttf, .otf ou o .zip baixado do Google Fonts)",
                    Filter = "Fontes|*.ttf;*.otf;*.zip|Todos os arquivos|*.*",
                    Multiselect = true,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int n = FontStore.Import(dlg.FileNames);
                    Send(new { type = "notice", message = n > 0 ? $"{n} arquivo(s) de fonte importado(s)" : "Nenhum arquivo .ttf ou .otf encontrado" });
                }
                catch (Exception ex) { Send(new { type = "error", message = $"Não consegui importar as fontes: {ex.Message}" }); }
                FontsChanged();
                break;
            }
            case "fontsList":
                SendFonts();
                break;
            case "fontsGoogle":
                _ = DownloadGoogleFontAsync((string?)msg["family"] ?? "");
                break;
            case "fontsDelete":
                if ((string?)msg["family"] is { } fam) { FontStore.Delete(fam); FontsChanged(); }
                break;
        }
    }

    async Task DownloadGoogleFontAsync(string family)
    {
        Send(new { type = "fontsBusy", family });
        try
        {
            int n = await FontStore.DownloadGoogleAsync(family);
            Send(new { type = "notice", message = $"Fonte {family.Trim()} instalada no VJApp ({n} estilos)" });
        }
        catch (Exception ex) { Send(new { type = "error", message = $"Google Fonts: {ex.Message}" }); }
        Send(new { type = "fontsBusy", family = (string?)null });
        FontsChanged();
    }

    void FontsChanged()
    {
        SendFonts();
        UpdateLyricsOutput(quiet: true); // redesenha o que está no telão com a fonte nova
    }
}
