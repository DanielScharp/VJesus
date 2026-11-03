using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Teste de identificação de LED, desenhado direto nas telas de saída (no lugar da imagem):
/// "régua" mostra a coordenada de cada pedaço da saída, para descobrir onde cada painel começa e
/// quanto mede olhando para o telão; "partes" pinta cada parte configurada com número, nome, medidas
/// e onde fica o topo, para conferir se o mapa bate com a processadora.
/// </summary>
sealed partial class MainForm
{
    string _ledTest = "off"; // off, regua, partes
    System.Windows.Forms.Timer? _ledTestTimer;

    void OnLedTestMessage(JsonNode msg)
    {
        string? mode = (string?)msg["mode"];
        _ledTest = mode is "regua" or "partes" ? mode : "off";
        ApplyLedTest();
        Send(new { type = "ledTest", mode = _ledTest });
    }

    /// <summary>O mapa mudou com o teste ligado: redesenha (com um pequeno atraso enquanto arrasta).</summary>
    void LedTestMappingChanged()
    {
        if (_ledTest == "off") return;
        _ledTestTimer ??= NewLedTestTimer();
        _ledTestTimer.Stop();
        _ledTestTimer.Start();
    }

    System.Windows.Forms.Timer NewLedTestTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 150 };
        t.Tick += (_, _) => { t.Stop(); ApplyLedTest(); };
        return t;
    }

    void ApplyLedTest()
    {
        if (_renderer == null) return;
        foreach (var screen in _mapping.Screens)
        {
            if (!_windows.TryGetValue(screen.Id, out var win)) continue;
            if (_ledTest == "off") { _renderer.SetOutputTest(win.Handle, null, 0, 0); continue; }
            var displays = Screen.AllScreens;
            var bounds = screen.Display >= 0 && screen.Display < displays.Length ? displays[screen.Display].Bounds : new Rectangle(0, 0, 1920, 1080);
            int w = Math.Clamp(bounds.Width, 64, 8192), h = Math.Clamp(bounds.Height, 64, 8192);
            var pixels = DrawLedTest(screen, w, h, _ledTest);
            _renderer.SetOutputTest(win.Handle, pixels, w, h);
        }
    }

    static readonly Color[] PartColors =
    [
        Color.FromArgb(230, 60, 60), Color.FromArgb(50, 160, 255), Color.FromArgb(60, 200, 90), Color.FromArgb(240, 180, 30),
        Color.FromArgb(180, 90, 230), Color.FromArgb(30, 200, 200), Color.FromArgb(255, 120, 180), Color.FromArgb(160, 160, 160),
    ];

    /// <summary>Desenha o teste em pixels da saída (os mesmos pixels que a processadora recebe).</summary>
    static byte[] DrawLedTest(OutputScreen screen, int w, int h, string mode)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.FromArgb(14, 14, 18));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var parts = screen.Led?.Parts ?? [];
            if (mode == "partes" && parts.Count > 0) DrawParts(g, parts, w, h);
            else DrawRuler(g, w, h, mode == "partes" ? "Nenhum painel de LED configurado nesta tela: mostrando a régua" : null);
        }
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * w * 4, w * 4);
        bmp.UnlockBits(data);
        return bytes;
    }

    /// <summary>
    /// Régua: quadros de 128 px com cores diferentes e a coordenada "x,y" no canto de cada um. No telão,
    /// o número no canto de cima à esquerda de um painel é onde ele começa; o do canto de baixo à direita
    /// (mais 128) dá o tamanho.
    /// </summary>
    static void DrawRuler(Graphics g, int w, int h, string? note)
    {
        const int Cell = 128;
        for (int cy = 0; cy * Cell < h; cy++)
            for (int cx = 0; cx * Cell < w; cx++)
            {
                var c = PartColors[(cx + cy * 3) % PartColors.Length];
                using var b = new SolidBrush(Color.FromArgb(70, c));
                g.FillRectangle(b, cx * Cell, cy * Cell, Cell, Cell);
            }
        using (var fine = new Pen(Color.FromArgb(40, 255, 255, 255)))
            for (int x = 0; x < w; x += 16) g.DrawLine(fine, x, 0, x, h);
        using (var fine = new Pen(Color.FromArgb(40, 255, 255, 255)))
            for (int y = 0; y < h; y += 16) g.DrawLine(fine, 0, y, w, y);
        using (var mid = new Pen(Color.FromArgb(110, 255, 255, 255)))
        {
            for (int x = 64; x < w; x += Cell) g.DrawLine(mid, x, 0, x, h);
            for (int y = 64; y < h; y += Cell) g.DrawLine(mid, 0, y, w, y);
        }
        using (var main = new Pen(Color.White))
        {
            for (int x = 0; x < w; x += Cell) g.DrawLine(main, x, 0, x, h);
            for (int y = 0; y < h; y += Cell) g.DrawLine(main, 0, y, w, y);
        }
        using var font = new Font("Consolas", 13, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        for (int y = 0; y < h; y += Cell)
            for (int x = 0; x < w; x += Cell)
            {
                string label = $"{x},{y}";
                var size = g.MeasureString(label, font);
                g.FillRectangle(bg, x + 2, y + 2, size.Width + 2, size.Height);
                g.DrawString(label, font, Brushes.White, x + 3, y + 2);
            }
        // borda da saída inteira em vermelho: se ela não aparece num lado, a saída é maior que os painéis
        using (var border = new Pen(Color.Red, 3)) g.DrawRectangle(border, 1, 1, w - 3, h - 3);
        string title = $"RÉGUA DE PIXELS · saída {w}×{h}";
        if (note != null) title += "\n" + note;
        using var big = new Font("Segoe UI", Math.Max(18, h / 40f), FontStyle.Bold, GraphicsUnit.Pixel);
        var t = g.MeasureString(title, big);
        g.FillRectangle(bg, (w - t.Width) / 2 - 8, (h - t.Height) / 2 - 4, t.Width + 16, t.Height + 8);
        using var center = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(title, big, Brushes.White, new RectangleF(0, (h - t.Height) / 2, w, t.Height), center);
    }

    /// <summary>Partes configuradas: cada uma com cor, número, nome, posição, tamanho e o topo da imagem.</summary>
    static void DrawParts(Graphics g, List<LedPart> parts, int w, int h)
    {
        using (var grid = new Pen(Color.FromArgb(35, 255, 255, 255)))
        {
            for (int x = 0; x < w; x += 64) g.DrawLine(grid, x, 0, x, h);
            for (int y = 0; y < h; y += 64) g.DrawLine(grid, 0, y, w, y);
        }
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var color = PartColors[i % PartColors.Length];
            var rect = new RectangleF(p.X, p.Y, p.W, p.H);
            using (var fill = new SolidBrush(Color.FromArgb(150, color))) g.FillRectangle(fill, rect);
            using (var cross = new Pen(Color.FromArgb(120, 255, 255, 255), 2))
            {
                g.DrawLine(cross, rect.Left, rect.Top, rect.Right, rect.Bottom);
                g.DrawLine(cross, rect.Right, rect.Top, rect.Left, rect.Bottom);
            }
            using (var edge = new Pen(Color.White, 3)) g.DrawRectangle(edge, rect.X + 1.5f, rect.Y + 1.5f, rect.Width - 3, rect.Height - 3);

            // o texto gira com a parte: o "TOPO" fica no lado de cima da imagem que aparece nela
            var state = g.Save();
            g.TranslateTransform(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            g.RotateTransform(((p.Rotation % 360) + 360) % 360);
            bool sideways = ((p.Rotation % 180) + 180) % 180 != 0;
            float bw = sideways ? rect.Height : rect.Width, bh = sideways ? rect.Width : rect.Height;
            var box = new RectangleF(-bw / 2, -bh / 2, bw, bh);
            float unit = Math.Max(10, Math.Min(bw, bh));
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var num = new Font("Segoe UI", unit * 0.32f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", Math.Max(11, unit * 0.075f), FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString((i + 1).ToString(), num, Brushes.White, new RectangleF(box.X, box.Y, box.Width, box.Height * 0.62f), center);
            string info = $"{p.Name}\nX {p.X:0}  Y {p.Y:0}\n{p.W:0} × {p.H:0} px" + (p.Rotation != 0 ? $"\ngirada {p.Rotation}°" : "");
            g.DrawString(info, small, Brushes.White, new RectangleF(box.X, box.Y + box.Height * 0.5f, box.Width, box.Height * 0.45f), center);
            using var top = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString("▲ TOPO", small, Brushes.White, new RectangleF(box.X, box.Y + 8, box.Width, small.Height * 1.4f), top);
            g.Restore(state);
        }
        using (var border = new Pen(Color.Red, 3)) g.DrawRectangle(border, 1, 1, w - 3, h - 3);
    }
}
