using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VJApp;

/// <summary>
/// Janela da tela de retorno (para os músicos). Mostra a mesma UI em modo "#stage" num WebView2
/// próprio; o conteúdo chega por mensagens do MainForm.
/// </summary>
sealed class StageWindow : Form
{
    readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    readonly Action _onReady;
    bool _ready;

    public bool AllowClose;

    public StageWindow(Action onReady)
    {
        _onReady = onReady;
        Text = $"{AppInfo.Name} - Retorno";
        BackColor = Color.Black;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(960, 540);
        Controls.Add(_web);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await _web.EnsureCoreWebView2Async();
        var core = _web.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.WebMessageReceived += (_, ev) =>
        {
            if (ev.WebMessageAsJson.Contains("\"stageHello\"")) { _ready = true; _onReady(); }
        };
        if (Environment.GetEnvironmentVariable("VJ_UI_DEV") == "1")
        {
            core.Navigate("http://localhost:5173/#stage");
        }
        else
        {
            string dist = Path.Combine(AppContext.BaseDirectory, "ui");
            core.SetVirtualHostNameToFolderMapping("app.vj", dist, CoreWebView2HostResourceAccessKind.Allow);
            Directory.CreateDirectory(VJApp.Engine.FontStore.Dir);
            core.SetVirtualHostNameToFolderMapping("fonts.vj", VJApp.Engine.FontStore.Dir, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://app.vj/index.html#stage");
        }
    }

    /// <summary>Manda o estado da tela de retorno (JSON) para a página.</summary>
    public void Post(string json)
    {
        if (_ready && _web.CoreWebView2 != null) _web.CoreWebView2.PostWebMessageAsJson(json);
    }

    public void PlaceOn(int displayIndex, bool fullscreen)
    {
        var screens = Screen.AllScreens;
        var screen = screens[Math.Clamp(displayIndex, 0, screens.Length - 1)];
        if (fullscreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen.Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(960, 540);
            Location = new Point(screen.WorkingArea.X + 120, screen.WorkingArea.Y + 120);
        }
        if (!Visible) Show();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }
}
