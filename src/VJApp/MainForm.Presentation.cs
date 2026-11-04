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
    const string PresentationFilter = "Apresentações (PowerPoint ou PDF)|*.pptx;*.ppt;*.ppsx;*.pps;*.pptm;*.pdf|Todos os arquivos|*.*";
    static readonly string[] PresentationExtensions = [".pptx", ".ppt", ".ppsx", ".pps", ".pptm", ".pdf"];

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
        Send(new { type = "notice", message = $"Preparando {Path.GetFileName(file)} (convertendo as páginas; pode levar alguns segundos)…" });
        try
        {
            var folder = await ConvertPresentationAsync(file);
            var labels = StepLabels(PresentationSlides(folder));
            int slides = labels.Count == 0 ? 0 : labels.Max(x => x.Slide);
            _comp.Layers[layer].Clips[column] = new Clip { Kind = ClipKind.Presentation, Path = folder, Name = Path.GetFileNameWithoutExtension(file) };
            if (_comp.ActiveHere(_comp.Layers[layer]) == column) ClearLayer(layer);
            CompositionChanged();
            string extra = labels.Count > slides ? $" ({labels.Count} etapas contando os cliques das animações)" : "";
            Send(new { type = "notice", message = $"{Path.GetFileNameWithoutExtension(file)}: {slides} slides prontos{extra}. Clique no clip para começar." });
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
        var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|etapas")))[..12].ToLowerInvariant();
        var folder = Path.Combine(PresentationsDir, $"{Slug(Path.GetFileNameWithoutExtension(file))}-{key}");
        if (PresentationSlides(folder).Length > 0) return Task.FromResult(folder); // já convertida

        // PDF: as páginas são desenhadas aqui mesmo (PDFium), sem precisar de outro programa
        if (Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return Task.Run(() => { ExportPdf(info.FullName, folder); return folder; });

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
                ExportSlideSteps(slide, tmp, n, w, h);
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

    static readonly object PdfLock = new(); // o PDFium não pode desenhar dois documentos ao mesmo tempo

    /// <summary>Cada página do PDF vira um PNG com fundo branco, no maior tamanho que cabe em 1920×1080.</summary>
    static void ExportPdf(string file, string folder)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "VJesus-slides-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            lock (PdfLock)
            {
                using var doc = Docnet.Core.DocLib.Instance.GetDocReader(file, new Docnet.Core.Models.PageDimensions(1080, 1920));
                int pages = doc.GetPageCount();
                if (pages == 0) throw new InvalidOperationException("o PDF não tem páginas");
                for (int i = 0; i < pages; i++)
                {
                    using var page = doc.GetPageReader(i);
                    int w = page.GetPageWidth(), h = page.GetPageHeight();
                    var bgra = page.GetImage(new Docnet.Core.Converters.NaiveTransparencyRemover(255, 255, 255));
                    using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * w * 4, data.Scan0 + y * data.Stride, w * 4);
                    bmp.UnlockBits(data);
                    bmp.Save(Path.Combine(tmp, $"slide{i + 1:000}.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            File.WriteAllText(Path.Combine(tmp, "origem.txt"), file);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            foreach (var f in Directory.GetFiles(tmp)) File.Copy(f, Path.Combine(folder, Path.GetFileName(f)));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"não consegui ler o PDF ({ex.Message.Trim()})");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>Uma animação que mostra ou esconde algo (a forma inteira ou um parágrafo dela).</summary>
    sealed record AnimStep(int Group, int ShapeZ, int Paragraph, bool Show);

    /// <summary>
    /// Exporta o slide como etapas: uma imagem para cada clique do PowerPoint, com tudo o que já entrou
    /// até aquele clique (e sem o que já saiu). O movimento da animação não vem junto; no VJesus cada
    /// etapa entra com a transição da layer. Sem animações de entrada ou saída, é uma imagem só.
    /// </summary>
    static void ExportSlideSteps(dynamic slide, string folder, int n, int w, int h)
    {
        var steps = new List<AnimStep>();
        int group = 0;
        try
        {
            dynamic seq = slide.TimeLine.MainSequence;
            for (int i = 1; i <= (int)seq.Count; i++)
            {
                dynamic e = seq.Item(i);
                if ((int)e.Timing.TriggerType == 1) group++; // novo clique
                bool exit = (int)e.Exit != 0;
                int type = (int)e.EffectType;
                // tipos 54 a 99 são ênfase e mídia, 100 em diante são trajetórias: não mostram nem escondem
                bool entrance = !exit && type is > 0 and < 54;
                if (!exit && !entrance) continue;
                int paragraph = 0;
                try { paragraph = (int)e.Paragraph; } catch { }
                steps.Add(new AnimStep(group, (int)e.Shape.ZOrderPosition, paragraph, entrance));
            }
        }
        catch { steps.Clear(); } // slide sem linha do tempo legível: exporta como está

        if (steps.Count == 0 || steps.Max(s => s.Group) == 0)
        {
            // nada muda com os cliques: o estado final (tudo que entra sozinho já entrou)
            slide.Export(Path.Combine(folder, $"slide{n:000}.png"), "PNG", w, h);
            return;
        }

        int clicks = steps.Max(s => s.Group);
        // quem começa escondido: o que tem uma entrada como primeira animação
        var targets = steps.Select(s => (s.ShapeZ, s.Paragraph)).Distinct().ToList();
        for (int k = 0; k <= clicks; k++)
        {
            dynamic copy = slide.Duplicate().Item(1);
            try
            {
                var hide = new List<dynamic>();
                foreach (var t in targets)
                {
                    // um parágrafo também obedece às animações da forma inteira
                    var mine = steps.Where(s => s.ShapeZ == t.ShapeZ && (s.Paragraph == t.Paragraph || s.Paragraph == 0)).ToList();
                    bool visible = !mine[0].Show;
                    foreach (var s in mine.Where(s => s.Group <= k)) visible = s.Show;
                    if (visible) continue;
                    dynamic? shape = null;
                    foreach (dynamic sh in copy.Shapes) if ((int)sh.ZOrderPosition == t.ShapeZ) { shape = sh; break; }
                    if (shape == null) continue;
                    if (t.Paragraph == 0) hide.Add(shape); // apagada depois (Visible = falso não vale na exportação)
                    else
                    {
                        // esconde só o parágrafo, sem mexer no lugar dos outros
                        dynamic para = shape.TextFrame2.TextRange.Paragraphs(t.Paragraph);
                        para.Font.Fill.Transparency = 1f;
                        try { para.ParagraphFormat.Bullet.Visible = 0; } catch { }
                    }
                }
                foreach (var shape in hide) shape.Delete(); // a cópia é descartada logo em seguida
                copy.Export(Path.Combine(folder, $"slide{n:000}-{k + 1:00}.png"), "PNG", w, h);
            }
            finally { copy.Delete(); }
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

    /// <summary>Rótulo de cada imagem: "slide002-03.png" vira "2.3" e "slide 2 · etapa 3 de 4".</summary>
    static List<(int Slide, string Label, string Long)> StepLabels(string[] files)
    {
        var parsed = files.Select(f =>
        {
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(f), @"slide(\d+)(?:-(\d+))?");
            return (Slide: m.Success ? int.Parse(m.Groups[1].Value) : 0, Step: m.Success && m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
        }).ToList();
        return [.. parsed.Select(p =>
        {
            int steps = parsed.Count(q => q.Slide == p.Slide);
            return steps > 1
                ? (p.Slide, $"{p.Slide}.{p.Step}", $"slide {p.Slide} · etapa {p.Step} de {steps}")
                : (p.Slide, $"{p.Slide}", $"slide {p.Slide}");
        })];
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
            var labels = StepLabels(slides);
            int current = _presSlide.GetValueOrDefault(l);
            list.Add(new
            {
                layer = l,
                layerName = _comp.Layers[l].Name,
                name = clip.Name,
                slide = current,
                count = slides.Length,
                slides = labels.Count == 0 ? 0 : labels.Max(x => x.Slide),
                labels = labels.Select(x => x.Label),
                current = current < labels.Count ? labels[current].Long : "",
                images = slides.Select(s => $"https://slides.vj/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(Path.GetFileName(s))}"),
            });
        }
        Send(new { type = "presentations", list });
    }
}
