using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using VJApp.Model;

namespace VJApp.Engine;

/// <summary>Animação contínua de um trecho (&lt;anim tipo intensidade velocidade&gt;).</summary>
sealed record WordAnim(string Type, float Intensity, float Speed);

/// <summary>
/// Um pedaço da imagem do slide que a GPU desenha e movimenta sozinho: uma palavra, uma linha,
/// um grupo animado ou o slide inteiro. Coordenadas em pixels da composição (1920×1080).
/// </summary>
sealed record LyricSprite(RectangleF Rect, bool FromAnimLayer, int Index, int Line, WordAnim? Anim);

/// <summary>
/// Um slide pronto para a GPU: as palavras paradas, as palavras animadas e a caixa de fundo em
/// imagens separadas (fundo transparente), mais a lista de pedaços e a transição de entrada.
/// </summary>
sealed class LyricsFrame
{
    public required byte[]? Static { get; init; }
    public required byte[]? Animated { get; init; }
    public required byte[]? Box { get; init; }
    public required List<LyricSprite> Sprites { get; init; }
    public required RectangleF BoxRect { get; init; }
    public required string Transition { get; init; }
    public required float TransitionSeconds { get; init; }
    public int WordCount { get; init; }
    public int LineCount { get; init; }
    public bool Empty => Sprites.Count == 0 && Box == null;
}

/// <summary>
/// A letra que está no telão. Desenha o slide (contorno, sombra, caixa, trechos formatados) em
/// imagens separadas e entrega para o motor, que anima a entrada/saída do slide e as palavras
/// marcadas com &lt;anim&gt; direto na GPU a cada quadro.
/// </summary>
sealed class LyricsFeed
{
    public const int Width = Renderer.CompWidth, Height = Renderer.CompHeight;
    const int Bytes = Width * Height * 4;

    readonly object _lock = new();
    LyricsFrame _frame = EmptyFrame("fade", 0.3f);
    int _version;
    int _renderSeq;

    static LyricsFrame EmptyFrame(string transition, float seconds) => new()
    {
        Static = null, Animated = null, Box = null, Sprites = [], BoxRect = RectangleF.Empty,
        Transition = transition, TransitionSeconds = seconds,
    };

    /// <summary>Muda o texto (null ou vazio = telão sem letra). O desenho acontece fora da thread da UI.</summary>
    public void Show(string? text, LyricStyle style)
    {
        int seq = Interlocked.Increment(ref _renderSeq);
        var s = style.Clone();
        Task.Run(() =>
        {
            var frame = Render(text, s);
            lock (_lock)
            {
                if (seq != _renderSeq) return; // já pediram outro texto
                _frame = frame;
                _version++;
            }
        });
    }

    /// <summary>Slide atual e uma versão que muda a cada slide novo. Render thread.</summary>
    public (int Version, LyricsFrame Frame) Snapshot()
    {
        lock (_lock) return (_version, _frame);
    }

    // ---------------- texto com formatação por trecho ----------------

    /// <summary>Um trecho de texto com a mesma formatação.</summary>
    sealed record Run(string Text, bool Bold, bool Italic, bool Underline, Color? Color, float Scale, WordAnim? Anim, string? Font = null);

    static readonly Regex Tag = new(@"<(/?)(b|i|u|cor|tam|anim|fonte)(?:\s+([^>]*))?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Lê as marcações de trecho: &lt;b&gt; negrito, &lt;i&gt; itálico, &lt;u&gt; sublinhado, &lt;cor #RRGGBB&gt; cor,
    /// &lt;tam 130%&gt; tamanho e &lt;anim balançar 1 1&gt; animação. Retorna as linhas, cada uma com seus trechos.
    /// </summary>
    static List<List<Run>> Parse(string text, LyricStyle style)
    {
        var lines = new List<List<Run>> { new() };
        bool bold = style.Bold, italic = style.Italic, underline = false;
        var colors = new Stack<Color?>();
        var scales = new Stack<float>();
        var anims = new Stack<WordAnim?>();
        var fonts = new Stack<string?>();
        string? font = null;
        Color? color = null;
        float scale = 1;
        WordAnim? anim = null;

        void Emit(string s)
        {
            if (style.Uppercase) s = s.ToUpperInvariant();
            var parts = s.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add([]);
                if (parts[i].Length > 0) lines[^1].Add(new Run(parts[i], bold, italic, underline, color, scale, anim, font));
            }
        }

        int pos = 0;
        foreach (Match m in Tag.Matches(text))
        {
            Emit(text[pos..m.Index]);
            pos = m.Index + m.Length;
            bool close = m.Groups[1].Value == "/";
            string arg = m.Groups[3].Value.Trim();
            switch (m.Groups[2].Value.ToLowerInvariant())
            {
                case "b": bold = close ? style.Bold : true; break;
                case "i": italic = close ? style.Italic : true; break;
                case "u": underline = !close; break;
                case "cor":
                    if (close) color = colors.Count > 0 ? colors.Pop() : null;
                    else { colors.Push(color); color = ParseColor(arg, 1); }
                    break;
                case "tam":
                    if (close) scale = scales.Count > 0 ? scales.Pop() : 1;
                    else
                    {
                        scales.Push(scale);
                        if (float.TryParse(arg.TrimEnd('%').Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                            scale = Math.Clamp(arg.EndsWith('%') ? pct / 100 : pct, 0.2f, 4f);
                    }
                    break;
                case "anim":
                    if (close) anim = anims.Count > 0 ? anims.Pop() : null;
                    else { anims.Push(anim); anim = ParseAnim(arg); }
                    break;
                case "fonte":
                    if (close) font = fonts.Count > 0 ? fonts.Pop() : null;
                    else { fonts.Push(font); font = arg.Trim().Trim('"') is { Length: > 0 } fname ? fname : null; }
                    break;
            }
        }
        Emit(text[pos..]);
        return lines;
    }

    /// <summary>"balançar 1.2 0.8" → tipo, intensidade (padrão 1) e velocidade (padrão 1).</summary>
    static WordAnim ParseAnim(string arg)
    {
        var parts = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float F(int i, float def) => parts.Length > i && float.TryParse(parts[i].Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0.05f, 5f) : def;
        string type = parts.Length > 0 ? Lyrics.Fold(parts[0]) : "balancar";
        return new WordAnim(type, F(1, 1), F(2, 1));
    }

    /// <summary>Remove as marcações (para medir/mostrar o texto puro).</summary>
    public static string StripTags(string text) => Tag.Replace(text, "");

    sealed record Piece(string Text, Run Run, float Size, float Width, bool Space);
    sealed record Line(List<Piece> Pieces, float Width, float Height, float Ascent);

    /// <summary>Transições que movem palavra por palavra ou linha por linha (precisam de um pedaço por palavra/linha).</summary>
    static bool PerWord(string t) => t is "palavra" or "maquina";
    static bool PerLine(string t) => t is "linha";

    /// <summary>Desenha o slide, diminuindo a fonte até caber, e separa os pedaços que a GPU vai mover.</summary>
    public static LyricsFrame Render(string? text, LyricStyle style)
    {
        string transition = Lyrics.Fold(style.Transition ?? "fade");
        float seconds = Math.Max(0, style.FadeMs / 1000f);
        if (string.IsNullOrWhiteSpace(StripTags(text ?? ""))) return EmptyFrame(transition, seconds);

        using var staticBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var animBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using var boxBmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var sprites = new List<LyricSprite>();
        bool anyAnim = false, anyBox = false;
        RectangleF boxRect = RectangleF.Empty;
        int wordCount = 0, lineCount;

        using (var gs = Graphics.FromImage(staticBmp))
        using (var ga = Graphics.FromImage(animBmp))
        using (var gb = Graphics.FromImage(boxBmp))
        {
            foreach (var g in new[] { gs, ga, gb })
            {
                g.Clear(System.Drawing.Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            }

            var (family, ownedFamily) = FontStore.Find(style.FontFamily);

            float margin = Math.Clamp(style.Margin, 0, Width / 3f);
            var area = new RectangleF(margin, margin * 0.6f, Width - 2 * margin, Height - 1.2f * margin);
            var paragraphs = Parse(text!, style);

            List<Line> lines = [];
            for (float size = Math.Max(12, style.FontSize); size >= 12; size *= 0.92f)
            {
                lines = Layout(gs, family, paragraphs, size, area.Width, style.LineSpacing);
                if (lines.Sum(l => l.Height) <= area.Height && lines.All(l => l.Width <= area.Width + 1)) break;
            }
            lineCount = lines.Count;

            float total = lines.Sum(l => l.Height);
            float y = style.Position switch
            {
                "top" => area.Top,
                "bottom" => area.Bottom - total,
                _ => area.Top + (area.Height - total) / 2,
            };

            // um contorno vetorial por palavra; as animadas vão para outra imagem
            var staticAll = new GraphicsPath();
            var animAll = new GraphicsPath();
            var colored = new List<(GraphicsPath Path, Color? Color, bool Anim)>();
            var words = new List<(RectangleF Rect, int Line, WordAnim? Anim)>();
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            float pad = style.OutlineWidth + (style.Shadow ? 8 : 2);
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                float x = style.Align switch
                {
                    "left" => area.Left,
                    "right" => area.Right - line.Width,
                    _ => area.Left + (area.Width - line.Width) / 2,
                };
                float baseline = y + line.Ascent;
                foreach (var p in line.Pieces)
                {
                    if (!p.Space)
                    {
                        var (ff, fs) = Face(family, p.Run);
                        float ascent = p.Size * ff.GetCellAscent(fs) / ff.GetEmHeight(fs);
                        var path = new GraphicsPath();
                        path.AddString(p.Text, ff, (int)fs, p.Size, new PointF(x, baseline - ascent), format);
                        bool isAnim = p.Run.Anim != null;
                        (isAnim ? animAll : staticAll).AddPath(path, false);
                        colored.Add((path, p.Run.Color, isAnim));
                        var b = path.GetBounds();
                        words.Add((RectangleF.Inflate(b, pad, pad), li, p.Run.Anim));
                        anyAnim |= isAnim;
                    }
                    x += p.Width;
                }
                y += line.Height;
            }
            wordCount = words.Count;

            var union = staticAll.GetBounds();
            var animBounds = animAll.GetBounds();
            if (animBounds.Width > 0) union = union.Width > 0 ? RectangleF.Union(union, animBounds) : animBounds;
            if (style.Box && union.Width > 0)
            {
                boxRect = RectangleF.Inflate(union, 30, 18);
                using var brush = new SolidBrush(ParseColor(style.BoxColor, style.BoxOpacity));
                using var rounded = Rounded(boxRect, 18);
                gb.FillPath(brush, rounded);
                anyBox = true;
            }

            DrawText(gs, staticAll, colored.Where(c => !c.Anim), style);
            if (anyAnim) DrawText(ga, animAll, colored.Where(c => c.Anim), style);
            foreach (var c in colored) c.Path.Dispose();
            staticAll.Dispose();
            animAll.Dispose();
            if (ownedFamily) family.Dispose();

            // pedaços parados: o slide inteiro, uma linha ou uma palavra cada (conforme a transição)
            var staticWords = words.Select((w, i) => (w, i)).Where(t => t.w.Anim == null).ToList();
            if (PerWord(transition))
                foreach (var (w, i) in staticWords) sprites.Add(new LyricSprite(w.Rect, false, i, w.Line, null));
            else if (PerLine(transition))
                foreach (var g in staticWords.GroupBy(t => t.w.Line))
                    sprites.Add(new LyricSprite(g.Select(t => t.w.Rect).Aggregate(RectangleF.Union), false, g.Key, g.Key, null));
            else if (staticWords.Count > 0)
                sprites.Add(new LyricSprite(new RectangleF(0, 0, Width, Height), false, 0, 0, null));

            // grupos animados: palavras seguidas, na mesma linha, com a mesma animação, se mexem juntas
            for (int i = 0; i < words.Count; i++)
            {
                if (words[i].Anim is not { } a) continue;
                var rect = words[i].Rect;
                int j = i;
                while (j + 1 < words.Count && words[j + 1].Anim == a && words[j + 1].Line == words[i].Line)
                    rect = RectangleF.Union(rect, words[++j].Rect);
                sprites.Add(new LyricSprite(rect, true, i, words[i].Line, a));
                i = j;
            }
        }

        return new LyricsFrame
        {
            Static = ToBytes(staticBmp),
            Animated = anyAnim ? ToBytes(animBmp) : null,
            Box = anyBox ? ToBytes(boxBmp) : null,
            Sprites = sprites,
            BoxRect = boxRect,
            Transition = transition,
            TransitionSeconds = seconds,
            WordCount = wordCount,
            LineCount = lineCount,
        };
    }

    static void DrawText(Graphics g, GraphicsPath all, IEnumerable<(GraphicsPath Path, Color? Color, bool Anim)> pieces, LyricStyle style)
    {
        if (style.Shadow)
        {
            using var shadow = (GraphicsPath)all.Clone();
            using var m = new Matrix();
            m.Translate(5, 6);
            shadow.Transform(m);
            using var sb = new SolidBrush(System.Drawing.Color.FromArgb(150, 0, 0, 0));
            g.FillPath(sb, shadow);
        }
        if (style.OutlineWidth > 0)
        {
            using var pen = new Pen(ParseColor(style.OutlineColor, 1), style.OutlineWidth * 2) { LineJoin = LineJoin.Round };
            g.DrawPath(pen, all);
        }
        var baseColor = ParseColor(style.Color, 1);
        foreach (var (path, color, _) in pieces)
        {
            using var fill = new SolidBrush(color ?? baseColor);
            g.FillPath(fill, path);
        }
    }

    static byte[] ToBytes(Bitmap bmp)
    {
        var pixels = new byte[Bytes];
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * Width * 4, Width * 4);
        bmp.UnlockBits(data);
        return pixels;
    }

    /// <summary>Família e estilo de um trecho (usa o arquivo próprio de negrito/itálico das fontes importadas).</summary>
    static (FontFamily Family, FontStyle Style) Face(FontFamily family, Run run)
    {
        // <fonte Nome>: o trecho usa outra família (importada ou do Windows)
        if (run.Font != null) family = FontStore.Shared(run.Font);
        return FontStore.Face(family, FontStyleFor(family, run));
    }

    static FontStyle FontStyleFor(FontFamily family, Run run)
    {
        var fs = FontStyle.Regular;
        if (run.Bold) fs |= FontStyle.Bold;
        if (run.Italic) fs |= FontStyle.Italic;
        if (run.Underline) fs |= FontStyle.Underline;
        if (family.IsStyleAvailable(fs)) return fs;
        return family.IsStyleAvailable(FontStyle.Regular) ? (run.Underline ? FontStyle.Underline : FontStyle.Regular) : FontStyle.Bold;
    }

    /// <summary>Quebra as linhas por palavra respeitando tamanhos diferentes dentro da mesma linha.</summary>
    static List<Line> Layout(Graphics g, FontFamily family, List<List<Run>> paragraphs, float size, float maxWidth, float spacing)
    {
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        var lines = new List<Line>();

        foreach (var para in paragraphs)
        {
            var pieces = new List<Piece>();
            foreach (var run in para)
            {
                float s = size * run.Scale;
                var (ff, fs) = Face(family, run);
                using var font = new Font(ff, s, fs, GraphicsUnit.Pixel);
                foreach (Match m in Regex.Matches(run.Text, @"\s+|\S+"))
                {
                    bool space = char.IsWhiteSpace(m.Value[0]);
                    float w = g.MeasureString(space ? " " : m.Value, font, PointF.Empty, format).Width;
                    pieces.Add(new Piece(m.Value, run, s, w, space));
                }
            }

            var current = new List<Piece>();
            float width = 0;
            void Flush()
            {
                while (current.Count > 0 && current[^1].Space) { width -= current[^1].Width; current.RemoveAt(current.Count - 1); }
                float maxSize = current.Count > 0 ? current.Max(p => p.Size) : size;
                float ascent = current.Count > 0
                    ? current.Max(p => { var (ff, fs) = Face(family, p.Run); return p.Size * ff.GetCellAscent(fs) / ff.GetEmHeight(fs); })
                    : size * 0.8f;
                var fsMain = FontStyle.Regular;
                float lineHeight = maxSize * family.GetLineSpacing(fsMain) / family.GetEmHeight(fsMain) * spacing;
                lines.Add(new Line([.. current], width, lineHeight, ascent));
                current = [];
                width = 0;
            }
            foreach (var p in pieces)
            {
                if (p.Space && current.Count == 0) continue;
                if (!p.Space && current.Count > 0 && width + p.Width > maxWidth) Flush();
                current.Add(p);
                width += p.Width;
            }
            Flush();
        }
        return lines;
    }

    static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Color ParseColor(string hex, float opacity)
    {
        Color c;
        try { c = ColorTranslator.FromHtml(hex.Trim()); }
        catch { c = System.Drawing.Color.White; }
        return System.Drawing.Color.FromArgb((int)(Math.Clamp(opacity, 0, 1) * 255), c.R, c.G, c.B);
    }
}
