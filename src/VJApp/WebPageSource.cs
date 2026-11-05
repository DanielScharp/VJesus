using System.Drawing.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using VJApp.Engine;

namespace VJApp;

/// <summary>
/// Página da internet como clip: um navegador escondido (fora da tela) do tamanho da composição, fotografado
/// várias vezes por segundo. "Mostrar para mexer" traz a janela para a tela (login, rolar, clicar) sem parar o telão.
/// </summary>
sealed class WebPageSource : FrameFeedSource
{
    const int W = Renderer.CompWidth, H = Renderer.CompHeight;
    readonly Form _form;
    readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 66 }; // ~15 fotos por segundo
    bool _capturing, _closed;
    public string Url { get; }
    public string? Error { get; private set; }

    public WebPageSource(string url, CoreWebView2Environment env)
    {
        Url = url;
        _form = new Form
        {
            Text = $"{AppInfo.Name} - página no telão (feche ou minimize para esconder; o telão continua)",
            Icon = AppInfo.Icon,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.Sizable,
            Location = new Point(-W - 4000, -H - 4000), // fora da tela: renderiza, mas ninguém vê
        };
        _form.Controls.Add(_web);
        _form.ClientSize = new Size(W, H);
        // fechar só esconde de novo (a página continua no telão até trocar o clip)
        _form.FormClosing += (_, e) => { if (_closed) return; e.Cancel = true; Hide(); };
        _form.Resize += (_, _) => { if (_form.WindowState == FormWindowState.Minimized) { _form.WindowState = FormWindowState.Normal; Hide(); } };
        _form.Show();
        _timer.Tick += async (_, _) => await Capture();
        _ = Start(env);
    }

    async Task Start(CoreWebView2Environment env)
    {
        try
        {
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _web.CoreWebView2.IsMuted = false;
            // links que abririam outra janela abrem na mesma página
            _web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; _web.CoreWebView2.Navigate(e.Uri); };
            _web.CoreWebView2.Navigate(Url);
            _timer.Start();
        }
        catch (Exception ex) { Error = ex.Message; }
    }

    async Task Capture()
    {
        if (_capturing || _closed || _web.CoreWebView2 == null) return;
        _capturing = true;
        try
        {
            using var ms = new MemoryStream();
            await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, ms);
            var bytes = ms.ToArray();
            // decodifica fora da tela do operador
            await Task.Run(() =>
            {
                using var bmp = new Bitmap(new MemoryStream(bytes));
                int w = bmp.Width, h = bmp.Height;
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                var px = new byte[w * h * 4];
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, px, y * w * 4, w * 4);
                bmp.UnlockBits(data);
                Push(px, w, h);
            });
        }
        catch { /* página carregando ou fechando */ }
        finally { _capturing = false; }
    }

    /// <summary>Traz a página para a tela, para mexer (login, rolar, clicar).</summary>
    public void Show()
    {
        if (_closed) return;
        var primary = Screen.PrimaryScreen!.WorkingArea;
        // cabe na tela principal mantendo 16:9 (o telão continua recebendo a página inteira)
        _form.Location = new Point(primary.Left + 40, primary.Top + 40);
        _form.Activate();
    }

    public void Hide()
    {
        if (_closed) return;
        _form.Location = new Point(-W - 4000, -H - 4000);
    }

    public override void Dispose()
    {
        base.Dispose();
        // o motor libera no render thread; a janela é do thread da tela
        if (_form.IsHandleCreated)
            _form.BeginInvoke(() =>
            {
                _closed = true;
                _timer.Stop();
                _timer.Dispose();
                _web.Dispose();
                _form.Close();
                _form.Dispose();
            });
    }
}
