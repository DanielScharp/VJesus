using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace VJApp;

/// <summary>
/// Conta-gotas de tela inteira: fotografa todas as telas e cobre cada monitor com a foto dele, com uma lupa
/// e o código da cor; o clique devolve a cor daquele ponto. Esc ou botão direito cancela. Uma janela por
/// monitor (cada uma na escala do seu monitor), para acertar o ponto mesmo com telas de tamanhos/escala
/// diferentes. Funciona em qualquer programa (o conta-gotas do navegador só enxerga a própria janela).
/// </summary>
sealed class ScreenColorPicker : Form
{
    readonly Bitmap _shot;       // foto de todas as telas (coordenadas físicas, a partir do canto da área virtual)
    readonly Rectangle _virt;    // área virtual (todas as telas)
    readonly Rectangle _screen;  // este monitor
    readonly Action<Color?> _done;
    public static readonly List<ScreenColorPicker> Open = [];

    const int Zoom = 12, Cells = 11;

    ScreenColorPicker(Bitmap shot, Rectangle virt, Rectangle screen, Action<Color?> done)
    {
        _shot = shot;
        _virt = virt;
        _screen = screen;
        _done = done;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Bounds = screen;
        Cursor = Cursors.Cross;
        DoubleBuffered = true;
        KeyPreview = true;
    }

    /// <summary>Abre o conta-gotas em todas as telas e espera o clique. null = cancelado.</summary>
    public static Color? Pick()
    {
        var virt = SystemInformation.VirtualScreen;
        using var shot = new Bitmap(virt.Width, virt.Height, PixelFormat.Format32bppRgb);
        using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(virt.Location, Point.Empty, virt.Size);
        Color? result = null;
        bool finished = false;
        void Done(Color? c)
        {
            if (finished) return;
            finished = true;
            result = c;
            foreach (var f in Open.ToList()) f.Close();
        }
        foreach (var s in Screen.AllScreens) Open.Add(new ScreenColorPicker(shot, virt, s.Bounds, Done));
        foreach (var f in Open) f.Show();
        Open.FirstOrDefault(f => f._screen.Contains(Cursor.Position))?.Activate();
        // espera aqui (como uma janela de diálogo), sem travar a tela
        while (!finished && Open.Any(f => !f.IsDisposed)) { Application.DoEvents(); Thread.Sleep(10); }
        foreach (var f in Open.ToList()) f.Dispose();
        Open.Clear();
        return result;
    }

    static Color ColorAt(Bitmap shot, Rectangle virt, Point screenPt)
    {
        int x = screenPt.X - virt.X, y = screenPt.Y - virt.Y;
        return x >= 0 && y >= 0 && x < shot.Width && y < shot.Height ? shot.GetPixel(x, y) : Color.Black;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // a foto deste monitor ocupando a janela inteira
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.DrawImage(_shot, new Rectangle(Point.Empty, ClientSize),
            new Rectangle(_screen.X - _virt.X, _screen.Y - _virt.Y, _screen.Width, _screen.Height), GraphicsUnit.Pixel);

        using var font = new Font("Segoe UI", 11, FontStyle.Bold);
        if (Screen.FromRectangle(_screen).Primary)
        {
            const string tip = "Clique na cor que você quer · Esc cancela";
            var tsz = g.MeasureString(tip, font);
            float tx = (ClientSize.Width - tsz.Width) / 2, ty = 20;
            using var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            g.FillRectangle(bg, tx - 12, ty - 6, tsz.Width + 24, tsz.Height + 12);
            g.DrawString(tip, font, Brushes.White, tx, ty);
        }

        var cur = Cursor.Position;
        if (!_screen.Contains(cur)) return;
        var c = ColorAt(_shot, _virt, cur);
        var m = PointToClient(cur);
        int size = Cells * Zoom;
        int lx = m.X + 24, ly = m.Y + 24;
        if (lx + size + 10 > ClientSize.Width) lx = m.X - size - 24;
        if (ly + size + 44 > ClientSize.Height) ly = m.Y - size - 60;
        g.FillRectangle(Brushes.Black, lx - 3, ly - 3, size + 6, size + 40);
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_shot, new Rectangle(lx, ly, size, size), new Rectangle(cur.X - _virt.X - Cells / 2, cur.Y - _virt.Y - Cells / 2, Cells, Cells), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, lx + Cells / 2 * Zoom, ly + Cells / 2 * Zoom, Zoom, Zoom);
        using (var b = new SolidBrush(c)) g.FillRectangle(b, lx, ly + size + 6, 28, 26);
        g.DrawString($"#{c.R:X2}{c.G:X2}{c.B:X2}", font, Brushes.White, lx + 34, ly + size + 8);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        foreach (var f in Open) f.Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _done(e.Button == MouseButtons.Left ? ColorAt(_shot, _virt, Cursor.Position) : null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) _done(null);
        else if (e.KeyCode == Keys.Enter) _done(ColorAt(_shot, _virt, Cursor.Position));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _done(null); // fechou de outro jeito (Alt+F4): cancela
    }
}
