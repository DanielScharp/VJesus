using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Apresentações do PowerPoint (.pptx, .ppt, .ppsx): o PowerPoint instalado no computador exporta cada
/// slide como imagem uma vez; depois a apresentação vira um clip da grade, passa slide a slide com a
/// transição da layer e funciona sem o PowerPoint aberto.
/// </summary>
sealed partial class MainForm
{
    const string PresentationFilter = "Apresentações do PowerPoint|*.pptx;*.ppt;*.ppsx;*.pps;*.pptm|Todos os arquivos|*.*";
    static readonly string[] PresentationExtensions = [".pptx", ".ppt", ".ppsx", ".pps", ".pptm"];

    /// <summary>%AppData%\VJesus\apresentacoes: uma pasta por apresentação, com um PNG por slide.</summary>
    static string PresentationsDir => Path.Combine(AppInfo.DataDir, "apresentacoes");

    /// <summary>Slide no ar em cada layer que está tocando uma apresentação.</summary>
    readonly Dictionary<int, int> _presSlide = [];

    static bool IsPresentationFile(string path) => PresentationExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    static string[] PresentationSlides(string? folder) =>
        folder != null && Directory.Exists(folder) ? [.. Directory.GetFiles(folder, "slide*.png").Order(StringComparer.OrdinalIgnoreCase)] : [];

    void OnPresentationMessage(string type, JsonNode msg)
    {
        int layer = (int?)msg["layer"] ?? -1;
        switch (type)
        {
            case "presentationBrowse":
            {
                using var dlg = new OpenFileDialog { Filter = PresentationFilter, Title = "Escolher apresentação" };
                if (dlg.ShowDialog(this) == DialogResult.OK) _ = ImportPresentationAsync(layer, (int?)msg["column"] ?? 0, dlg.FileName);
                break;
            }
            case "presentationImport":
                // arquivo já escolhido (ex.: arrastado de outra parte do programa)
                if ((string?)msg["path"] is { } path && File.Exists(path) && IsPresentationFile(path))
                    _ = ImportPresentationAsync(layer, (int?)msg["column"] ?? 0, path);
                break;
            case "presentationStep":
                PresentationStep(layer, (int?)msg["direction"] ?? 1);
                break;
            case "presentationGo":
                PresentationGo(layer, (int?)msg["slide"] ?? 0);
                break;
        }
    }

    /// <summary>Converte (se ainda não foi) e coloca a apresentação na célula pedida.</summary>
    async Task ImportPresentationAsync(int layer, int column, string file)
    {
        if (layer < 0 || layer >= _comp.Layers.Count || column < 0 || column >= _comp.Columns) return;
        Send(new { type = "notice", message = $"Preparando {Path.GetFileName(file)} (o PowerPoint exporta os slides; pode levar alguns segundos)…" });
        try
        {
            var folder = await ConvertPresentationAsync(file);
            int count = PresentationSlides(folder).Length;
            _comp.Layers[layer].Clips[column] = new Clip { Kind = ClipKind.Presentation, Path = folder, Name = Path.GetFileNameWithoutExtension(file) };
            if (_comp.ActiveHere(_comp.Layers[layer]) == column) ClearLayer(layer);
            CompositionChanged();
            Send(new { type = "notice", message = $"{Path.GetFileNameWithoutExtension(file)}: {count} slides prontos. Clique no clip para começar." });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui abrir a apresentação: {ex.Message}" });
        }
    }

    /// <summary>Exporta cada slide para PNG (uma vez por versão do arquivo) e devolve a pasta.</summary>
    static Task<string> ConvertPresentationAsync(string file)
    {
        var info = new FileInfo(file);
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..12].ToLowerInvariant();
        var folder = Path.Combine(PresentationsDir, $"{Slug(Path.GetFileNameWithoutExtension(file))}-{key}");
        if (PresentationSlides(folder).Length > 0) return Task.FromResult(folder); // já convertida

        var done = new TaskCompletionSource<string>();
        // o PowerPoint é COM: precisa de uma thread STA própria (fora da interface)
        var thread = new Thread(() =>
        {
            try { ExportSlides(info.FullName, folder); done.SetResult(folder); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "PowerPoint" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    static string Slug(string name)
    {
        var s = new string(name.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray()).Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length == 0 ? "apresentacao" : s[..Math.Min(40, s.Length)];
    }

    static void ExportSlides(string file, string folder)
    {
        var type = Type.GetTypeFromProgID("PowerPoint.Application")
            ?? throw new InvalidOperationException("é preciso ter o Microsoft PowerPoint instalado neste computador para converter os slides");
        bool wasRunning = Process.GetProcessesByName("POWERPNT").Length > 0;
        dynamic? app = null, pres = null;
        // exporta numa pasta temporária simples (o PowerPoint recusa alguns caminhos) e copia no fim
        var tmp = Path.Combine(Path.GetTempPath(), "VJesus-slides-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            app = Activator.CreateInstance(type)!;
            // somente leitura, sem título novo, sem janela
            pres = app.Presentations.Open(file, -1, 0, 0);
            float sw = pres.PageSetup.SlideWidth, sh = pres.PageSetup.SlideHeight;
            // o maior tamanho que cabe em 1920×1080 mantendo a proporção do slide
            double scale = Math.Min(1920 / sw, 1080 / sh);
            int w = (int)Math.Round(sw * scale), h = (int)Math.Round(sh * scale);
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            int n = 0, total = pres.Slides.Count;
            for (int i = 1; i <= total; i++)
            {
                dynamic slide = pres.Slides.Item(i);
                if ((int)slide.SlideShowTransition.Hidden == -1) continue; // slide oculto não entra
                n++;
                slide.Export(Path.Combine(tmp, $"slide{n:000}.png"), "PNG", w, h);
            }
            if (n == 0) throw new InvalidOperationException("a apresentação não tem slides visíveis");
            File.WriteAllText(Path.Combine(tmp, "origem.txt"), file);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.GetFiles(tmp)) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"o PowerPoint não conseguiu abrir o arquivo ({ex.Message.Trim()})");
        }
        finally
        {
            try { pres?.Close(); } catch { }
            try { if (!wasRunning) app?.Quit(); } catch { }
            if (pres != null) Marshal.FinalReleaseComObject(pres);
            if (app != null) Marshal.FinalReleaseComObject(app);
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>Abre o slide <paramref name="index"/> na layer (com a transição da layer).</summary>
    bool ShowPresentationSlide(int layer, Clip clip, int index)
    {
        var slides = PresentationSlides(clip.Path);
        if (slides.Length == 0)
        {
            Send(new { type = "error", message = $"Os slides de \"{clip.Name}\" não foram encontrados. Coloque a apresentação de novo na grade." });
            return false;
        }
        index = Math.Clamp(index, 0, slides.Length - 1);
        VideoPlayer player;
        try { player = new VideoPlayer(slides[index]); }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui abrir o slide {index + 1}: {ex.Message}" });
            return false;
        }
        _players[layer] = player;
        _renderer!.SetLayerSource(layer, SourceKind.Video, player);
        _presSlide[layer] = index;
        return true;
    }

    /// <summary>Layer tocando uma apresentação (a de cima, se houver várias), ou -1.</summary>
    int PresentationLayer(int preferred = -1)
    {
        if (preferred >= 0 && preferred < _comp.Layers.Count && _comp.Layers[preferred].PlayingClip?.Kind == ClipKind.Presentation) return preferred;
        for (int l = _comp.Layers.Count - 1; l >= 0; l--)
            if (_comp.Layers[l].PlayingClip?.Kind == ClipKind.Presentation) return l;
        return -1;
    }

    /// <summary>Próximo/anterior slide. Falso quando já está no último (ou no primeiro).</summary>
    bool PresentationStep(int layer, int direction)
    {
        layer = PresentationLayer(layer);
        if (layer < 0 || _comp.Layers[layer].PlayingClip is not { } clip) return false;
        int count = PresentationSlides(clip.Path).Length;
        int current = _presSlide.GetValueOrDefault(layer);
        int next = current + direction;
        if (next < 0 || next >= count) return false;
        if (ShowPresentationSlide(layer, clip, next)) { ApplyClipProps(layer, clip); SendPresentations(); }
        return true;
    }

    void PresentationGo(int layer, int slide)
    {
        layer = PresentationLayer(layer);
        if (layer < 0 || _comp.Layers[layer].PlayingClip is not { } clip) return;
        if (ShowPresentationSlide(layer, clip, slide)) { ApplyClipProps(layer, clip); SendPresentations(); }
    }

    /// <summary>Estado para a barra de apresentação: o que está no ar e as miniaturas (servidas em https://slides.vj/).</summary>
    void SendPresentations()
    {
        var list = new List<object>();
        for (int l = 0; l < _comp.Layers.Count; l++)
        {
            if (_comp.Layers[l].PlayingClip is not { Kind: ClipKind.Presentation } clip) continue;
            var slides = PresentationSlides(clip.Path);
            string folder = Path.GetFileName(clip.Path ?? "");
            list.Add(new
            {
                layer = l,
                layerName = _comp.Layers[l].Name,
                name = clip.Name,
                slide = _presSlide.GetValueOrDefault(l),
                count = slides.Length,
                images = slides.Select(s => $"https://slides.vj/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(Path.GetFileName(s))}"),
            });
        }
        Send(new { type = "presentations", list });
    }
}
