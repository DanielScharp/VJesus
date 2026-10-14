using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using VJApp.Model;

namespace VJApp.Engine;

/// <summary>
/// A letra que está no telão: desenha o texto (contorno, sombra, caixa, trechos com formatação
/// própria) numa imagem 1920×1080 com fundo transparente e faz o fade entre um slide e outro.
/// Qualquer layer com um clip "Letras" mostra esta mesma imagem, misturada com os vídeos.
/// </summary>
sealed class LyricsFeed
{
    public const int Width = Renderer.CompWidth, Height = Renderer.CompHeight;
    const int Bytes = Width * Height * 4;

    readonly object _lock = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    byte[] _from = new byte[Bytes];   // imagem antes do fade
    byte[] _to = new byte[Bytes];     // imagem depois do fade
    readonly byte[] _mixed = new byte[Bytes];
    double _fadeStart, _fadeSeconds;
    int _version, _mixedStep = -1;
    int _renderSeq;

    /// <summary>Muda o texto (null ou vazio = telão sem letra). O desenho acontece fora da thread da UI.</summary>
    public void Show(string? text, LyricStyle style)
    {
        int seq = Interlocked.Increment(ref _renderSeq);
        var styleCopy = style.Clone();
        Task.Run(() =>
        {
            var pixels = Render(text, styleCopy);
            lock (_lock)
            {
                if (seq != _renderSeq) return; // já pediram outro texto
                // começa o fade a partir do que está na tela agora
                CurrentPixels().CopyTo(_from, 0);
                _to = pixels;
                _fadeStart = _clock.Elapsed.TotalSeconds;
                _fadeSeconds = Math.Max(0, styleCopy.FadeMs / 1000.0);
                _mixedStep = -1;
                _version++;
            }
        });
    }

    /// <summary>Imagem atual (com o fade calculado) e uma versão que muda quando a imagem muda. Render thread.</summary>
    public (int Version, byte[] Pixels) Snapshot()
    {
        lock (_lock)
        {
            double t = _fadeSeconds <= 0 ? 1 : (_clock.Elapsed.TotalSeconds - _fadeStart) / _fadeSeconds;
            if (t >= 1) return (_version * 64 + 63, _to);
            // fade em 30 passos: cada passo é uma versão nova
            int step = (int)(Math.Clamp(t, 0, 1) * 30);
            if (step != _mixedStep)
            {
                Mix(_from, _to, _mixed, step / 30f);
                _mixedStep = step;
            }
            return (_version * 64 + step, _mixed);
        }
    }

    byte[] CurrentPixels()
    {
        double t = _fadeSeconds <= 0 ? 1 : (_clock.Elapsed.TotalSeconds - _fadeStart) / _fadeSeconds;
        if (t >= 1 || _mixedStep < 0) return _to;
        return _mixed;
    }

    static void Mix(byte[] a, byte[] b, byte[] dst, float t)
    {
        int k = (int)(t * 256);
        int ik = 256 - k;
        Parallel.For(0, Height, y =>
        {
            int start = y * Width * 4, end = start + Width * 4;
            for (int i = start; i < end; i++) dst[i] = (byte)((a[i] * ik + b[i] * k) >> 8);
        });
    }

    // ---------------- texto com formatação por trecho ----------------

    /// <summary>Um trecho de texto com a mesma formatação.</summary>
    sealed record Run(string Text, bool Bold, bool Italic, bool Underline, Color? Color, float Scale);

    static readonly Regex Tag = new(@"<(/?)(b|i|u|cor|tam)(?:\s+([^>]*))?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Lê as marcações de trecho: &lt;b&gt; negrito, &lt;i&gt; itálico, &lt;u&gt; sublinhado,
    /// &lt;cor #RRGGBB&gt; cor e &lt;tam 130%&gt; tamanho relativo. Retorna uma lista de linhas, cada uma com seus trechos.
    /// </summary>
    static List<List<Run>> Parse(string text, LyricStyle style)
    {
        var lines = new List<List<Run>> { new() };
        bool bold = style.Bold, italic = style.Italic, underline = false;
        var colors = new Stack<Color?>();
        var scales = new Stack<float>();
        Color? color = null;
        float scale = 1;

        void Emit(string s)
        {
            if (style.Uppercase) s = s.ToUpperInvariant();
            var parts = s.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add([]);
                if (parts[i].Length > 0) lines[^1].Add(new Run(parts[i], bold, italic, underline, color, scale));
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
                        if (float.TryParse(arg.TrimEnd('%').Replace(',', '.'), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pct))
                            scale = Math.Clamp(arg.EndsWith('%') ? pct / 100 : pct, 0.2f, 4f);
                    }
                    break;
            }
        }
        Emit(text[pos..]);
        return lines;
    }

    /// <summary>Remove as marcações (para medir/mostrar o texto puro).</summary>
    public static string StripTags(string text) => Tag.Replace(text, "");

    sealed record Piece(string Text, Run Run, float Size, float Width, bool Space);
    sealed record Line(List<Piece> Pieces, float Width, float Height, float Ascent);

    /// <summary>Desenha o texto, diminuindo a fonte até caber. BGRA com alpha.</summary>
    public static byte[] Render(string? text, LyricStyle style)
    {
        var pixels = new byte[Bytes];
        if (string.IsNullOrWhiteSpace(StripTags(text ?? ""))) return pixels;
        using var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            FontFamily family;
            try { family = new FontFamily(style.FontFamily); }
            catch { family = FontFamily.GenericSansSerif; }

            float margin = Math.Clamp(style.Margin, 0, Width / 3f);
            var area = new RectangleF(margin, margin * 0.6f, Width - 2 * margin, Height - 1.2f * margin);
            var paragraphs = Parse(text!, style);

            // reduz o tamanho até caber na área
            List<Line> lines = [];
            for (float size = Math.Max(12, style.FontSize); size >= 12; size *= 0.92f)
            {
                lines = Layout(g, family, paragraphs, size, area.Width, style.LineSpacing);
                if (lines.Sum(l => l.Height) <= area.Height && lines.All(l => l.Width <= area.Width + 1)) break;
            }

            float total = lines.Sum(l => l.Height);
            float y = style.Position switch
            {
                "top" => area.Top,
                "bottom" => area.Bottom - total,
                _ => area.Top + (area.Height - total) / 2,
            };

            // monta um contorno vetorial por trecho (cada um com sua cor)
            var all = new GraphicsPath();
            var colored = new List<(GraphicsPath Path, Color? Color)>();
            using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            foreach (var line in lines)
            {
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
                        var fs = FontStyleFor(family, p.Run);
                        float ascent = p.Size * family.GetCellAscent(fs) / family.GetEmHeight(fs);
                        var path = new GraphicsPath();
                        path.AddString(p.Text, family, (int)fs, p.Size, new PointF(x, baseline - ascent), format);
                        all.AddPath(path, false);
                        colored.Add((path, p.Run.Color));
                    }
                    x += p.Width;
                }
                y += line.Height;
            }

            var bounds = all.GetBounds();
            if (style.Box && bounds.Width > 0)
            {
                var box = RectangleF.Inflate(bounds, 30, 18);
                using var brush = new SolidBrush(ParseColor(style.BoxColor, style.BoxOpacity));
                using var rounded = Rounded(box, 18);
                g.FillPath(brush, rounded);
            }
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
            foreach (var (path, color) in colored)
            {
                using var fill = new SolidBrush(color ?? baseColor);
                g.FillPath(fill, path);
                path.Dispose();
            }
            all.Dispose();
            family.Dispose();
        }

        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        for (int y = 0; y < Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * Width * 4, Width * 4);
        bmp.UnlockBits(data);
        return pixels;
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
            // palavras e espaços como peças separadas
            var pieces = new List<Piece>();
            foreach (var run in para)
            {
                float s = size * run.Scale;
                var fs = FontStyleFor(family, run);
                using var font = new Font(family, s, fs, GraphicsUnit.Pixel);
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
                    ? current.Max(p => { var fs = FontStyleFor(family, p.Run); return p.Size * family.GetCellAscent(fs) / family.GetEmHeight(fs); })
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
