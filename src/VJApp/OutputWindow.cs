namespace VJApp;

/// <summary>
/// Janela de saída (projetor). Pode ficar em janela ou em tela cheia sem borda num monitor escolhido.
/// </summary>
sealed class OutputWindow : Form
{
    public OutputWindow()
    {
        Text = $"{AppInfo.Name} - Saída";
        Icon = AppInfo.Icon;
        BackColor = Color.Black;
        ClientSize = new Size(960, 540);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(80, 80);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
    }

    public bool AllowClose;

    // impede o usuário de fechar a saída sem querer; ela fecha junto com o app
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!AllowClose) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    public void PlaceOn(int displayIndex, bool fullscreen)
    {
        var screens = Screen.AllScreens;
        var screen = screens[Math.Clamp(displayIndex, 0, screens.Length - 1)];
        if (fullscreen)
        {
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen.Bounds;
            TopMost = true;
        }
        else
        {
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            ClientSize = new Size(960, 540);
            Location = new Point(screen.WorkingArea.X + 80, screen.WorkingArea.Y + 80);
        }
        Show();
    }
}
