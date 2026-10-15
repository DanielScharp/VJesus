using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VJApp.Model;

/// <summary>Uma música da biblioteca. O texto é dividido em slides por linhas em branco.</summary>
sealed class Song
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    /// <summary>
    /// Letra completa. Linha em branco separa slides. No começo de cada slide podem vir:
    /// <c>[Refrão]</c> (rótulo), <c>{cena: Nome}</c> (cena só deste slide) e
    /// <c>{estilo: tamanho=110; cor=#FFCC00; posicao=bottom}</c> (ajustes só deste slide).
    /// No meio do texto: &lt;b&gt;, &lt;i&gt;, &lt;u&gt;, &lt;cor #RRGGBB&gt; e &lt;tam 130%&gt; formatam trechos.
    /// </summary>
    public string Text { get; set; } = "";
    /// <summary>Cena aplicada à música inteira (null = estilo padrão).</summary>
    public string? SceneId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

/// <summary>Um slide pronto para projetar.</summary>
sealed record LyricSlide(string? Label, string? Scene, Dictionary<string, string> Overrides, string Text);

/// <summary>Uma cena: um estilo de texto com nome, reaproveitável em várias músicas e slides.</summary>
sealed class LyricScene
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Cena";
    public LyricStyle Style { get; set; } = new();
}

/// <summary>Aparência do texto projetado.</summary>
sealed class LyricStyle
{
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 80;
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }
    public bool Uppercase { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public string OutlineColor { get; set; } = "#000000";
    public float OutlineWidth { get; set; } = 6;
    public bool Shadow { get; set; } = true;
    public bool Box { get; set; }
    public string BoxColor { get; set; } = "#000000";
    public float BoxOpacity { get; set; } = 0.55f;
    /// <summary>"top", "center" ou "bottom".</summary>
    public string Position { get; set; } = "center";
    /// <summary>"left", "center" ou "right".</summary>
    public string Align { get; set; } = "center";
    /// <summary>Margem lateral/vertical em pixels da composição (1920×1080).</summary>
    public float Margin { get; set; } = 120;
    public float LineSpacing { get; set; } = 1.1f;
    /// <summary>Duração da transição entre slides (ms).</summary>
    public int FadeMs { get; set; } = 300;
    /// <summary>
    /// Como o slide entra: fade, corte, subir, descer, esquerda, direita, zoom, aproximar,
    /// palavra (palavra por palavra), linha (linha por linha) ou maquina (máquina de escrever).
    /// </summary>
    public string Transition { get; set; } = "fade";

    public LyricStyle Clone() => (LyricStyle)MemberwiseClone();

    /// <summary>Aplica os ajustes de um slide (<c>{estilo: chave=valor; ...}</c>) sobre uma cópia.</summary>
    public LyricStyle With(Dictionary<string, string> o)
    {
        var s = Clone();
        foreach (var (rawKey, value) in o)
        {
            string key = Lyrics.Fold(rawKey);
            float F() => float.Parse(value.Replace(',', '.').TrimEnd('%', 'p', 'x'), CultureInfo.InvariantCulture);
            bool B() => Lyrics.Fold(value) is "sim" or "s" or "true" or "1" or "on";
            try
            {
                switch (key)
                {
                    case "tamanho": s.FontSize = F(); break;
                    case "fonte": s.FontFamily = value; break;
                    case "cor": s.Color = value; break;
                    case "contorno": s.OutlineColor = value; break;
                    case "espessura": s.OutlineWidth = F(); break;
                    case "posicao": s.Position = Lyrics.Fold(value) switch { "cima" or "top" => "top", "baixo" or "bottom" => "bottom", _ => "center" }; break;
                    case "alinhamento": s.Align = Lyrics.Fold(value) switch { "esquerda" or "left" => "left", "direita" or "right" => "right", _ => "center" }; break;
                    case "negrito": s.Bold = B(); break;
                    case "italico": s.Italic = B(); break;
                    case "maiusculas": s.Uppercase = B(); break;
                    case "sombra": s.Shadow = B(); break;
                    case "caixa": s.Box = B(); break;
                    case "corcaixa": s.BoxColor = value; break;
                    case "margem": s.Margin = F(); break;
                    case "transicao": s.Transition = Lyrics.Fold(value); break;
                    case "duracao": s.FadeMs = (int)F(); break;
                }
            }
            catch { /* valor inválido: ignora este ajuste */ }
        }
        return s;
    }
}

sealed class LyricsLibrary
{
    public List<Song> Songs { get; set; } = [];
    /// <summary>Estilo padrão (usado quando a música não tem cena).</summary>
    public LyricStyle Style { get; set; } = new();
    public List<LyricScene> Scenes { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp", "letras.json");

    public static LyricsLibrary LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<LyricsLibrary>(File.ReadAllText(path), JsonOptions) is { } lib)
            {
                if (lib.Scenes.Count == 0) lib.Scenes = DefaultScenes();
                return lib;
            }
        }
        catch { /* arquivo corrompido: começa do zero */ }

        // biblioteca nova: uma música de exemplo (texto próprio) para mostrar o formato
        return new LyricsLibrary
        {
            Scenes = DefaultScenes(),
            Songs =
            [
                new Song
                {
                    Title = "Exemplo de formato",
                    Artist = "VJApp",
                    Text = "[Verso 1]\nCada slide é um bloco de linhas\nSepare os blocos com uma linha em branco\n\n" +
                           "[Refrão]\n{cena: Destaque}\nUse colchetes para nomear a seção\nO <cor #FFCC00>rótulo</cor> não aparece no telão\n\n" +
                           "[Final]\nClique num slide para projetar\nSetas do teclado passam para o <b>próximo</b>",
                },
            ],
        };
    }

    static List<LyricScene> DefaultScenes() =>
    [
        new LyricScene { Name = "Destaque", Style = new LyricStyle { FontSize = 110, Color = "#FFE082", Uppercase = true } },
        new LyricScene { Name = "Rodapé", Style = new LyricStyle { FontSize = 64, Position = "bottom", Box = true, Margin = 80 } },
    ];

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Estilo final de um slide: padrão → cena da música → cena do slide → ajustes do slide.</summary>
    public LyricStyle ResolveStyle(Song? song, LyricSlide? slide)
    {
        var style = Style;
        if (song?.SceneId is { } sid && Scenes.FirstOrDefault(s => s.Id == sid) is { } songScene) style = songScene.Style;
        if (slide?.Scene is { } name && Scenes.FirstOrDefault(s => Lyrics.Fold(s.Name) == Lyrics.Fold(name)) is { } slideScene)
            style = slideScene.Style;
        return slide is { Overrides.Count: > 0 } ? style.With(slide.Overrides) : style.Clone();
    }

    /// <summary>Divide a letra em slides (mesma regra da UI em ui/src/lyrics.ts).</summary>
    public static List<LyricSlide> Slides(string text) => Lyrics.Slides(text);
}

static class Lyrics
{
    static readonly Regex BlankLines = new(@"\n\s*\n", RegexOptions.Compiled);
    static readonly Regex LabelLine = new(@"^\s*\[(.+?)\]\s*$", RegexOptions.Compiled);
    static readonly Regex DirectiveLine = new(@"^\s*\{\s*([^:}]+?)\s*:\s*([^}]*)\}\s*$", RegexOptions.Compiled);

    public static List<LyricSlide> Slides(string text)
    {
        var slides = new List<LyricSlide>();
        foreach (var block in BlankLines.Split(text.Replace("\r\n", "\n")))
        {
            var lines = block.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            string? label = null, scene = null;
            var overrides = new Dictionary<string, string>();
            // cabeçalho do slide: rótulo e diretivas, em qualquer ordem
            while (lines.Count > 0)
            {
                Match m;
                if ((m = LabelLine.Match(lines[0])).Success) label = m.Groups[1].Value;
                else if ((m = DirectiveLine.Match(lines[0])).Success)
                {
                    string key = Fold(m.Groups[1].Value);
                    if (key == "cena") scene = m.Groups[2].Value.Trim();
                    else if (key == "estilo")
                        foreach (var part in m.Groups[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var kv = part.Split('=', 2);
                            if (kv.Length == 2) overrides[kv[0].Trim()] = kv[1].Trim();
                        }
                }
                else break;
                lines.RemoveAt(0);
            }
            if (lines.Count == 0) continue;
            slides.Add(new LyricSlide(label, scene, overrides, string.Join("\n", lines)));
        }
        return slides;
    }

    /// <summary>Minúsculas e sem acento (para comparar nomes e chaves).</summary>
    public static string Fold(string s)
    {
        var d = s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}
