namespace VJApp;

/// <summary>
/// Em qual monitor cada saída (e a tela de retorno) abre. O monitor fica guardado pelo nome do Windows
/// (\\.\DISPLAY2), não pela posição na lista, que muda quando um cabo é ligado em outra ordem ou o
/// Windows reinicia. Regras ao abrir:
/// - achou o monitor guardado: usa ele (inclusive a tela principal, se foi a pessoa que escolheu);
/// - não achou (desligado, ou configuração antiga sem nome): usa outro monitor que não seja o principal;
/// - só existe uma tela: a saída abre em janela, nunca em tela cheia por cima do VJesus
///   (a não ser que a pessoa ligue a tela cheia agora, nesta sessão).
/// </summary>
sealed partial class MainForm
{
    /// <summary>Saídas em que a pessoa ligou tela cheia nesta sessão com um monitor só (aí vale).</summary>
    readonly HashSet<string> _singleScreenFullscreenOk = [];
    bool _displayEventsHooked;

    /// <summary>Índice do monitor para um monitor guardado (nome + índice antigo) e se a tela cheia deve ser segurada.</summary>
    static (int Index, bool ForceWindowed) ResolveDisplay(string? deviceName, int savedIndex)
    {
        var screens = Screen.AllScreens;
        if (screens.Length <= 1) return (0, true);
        if (deviceName != null)
        {
            int i = Array.FindIndex(screens, s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) return (i, false);
        }
        // configuração antiga (sem nome) apontando para outro monitor que não o principal: mantém
        if (deviceName == null && savedIndex >= 0 && savedIndex < screens.Length && !screens[savedIndex].Primary) return (savedIndex, false);
        // monitor guardado não está ligado: vai para o primeiro que não é o principal
        return (Array.FindIndex(screens, s => !s.Primary), false);
    }

    /// <summary>Nome do Windows do monitor no índice (para guardar a escolha da pessoa).</summary>
    static string? DisplayNameAt(int index)
    {
        var screens = Screen.AllScreens;
        return index >= 0 && index < screens.Length ? screens[index].DeviceName : null;
    }

    /// <summary>Quando um monitor é ligado/desligado ou muda de resolução: recoloca as saídas e avisa a interface.</summary>
    void HookDisplayEvents()
    {
        if (_displayEventsHooked) return;
        _displayEventsHooked = true;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                _placement.Clear();
                _stagePlacement = null;
                ApplyMapping();
                ApplyStageWindow();
                SendDisplays();
                SendMapping();
            });
        };
    }

    void SendDisplays() => Send(new
    {
        type = "displays",
        list = Screen.AllScreens.Select((s, i) => new
        {
            index = i,
            name = s.DeviceName.Replace(@"\\.\", ""),
            width = s.Bounds.Width,
            height = s.Bounds.Height,
            primary = s.Primary,
        }),
    });

    /// <summary>O VJesus abre sempre na tela principal (as saídas é que vão para os outros monitores).</summary>
    void PlaceMainWindowOnPrimary()
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        var size = new Size(Math.Min(Width, wa.Width), Math.Min(Height, wa.Height));
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(wa.X + (wa.Width - size.Width) / 2, wa.Y + (wa.Height - size.Height) / 2, size.Width, size.Height);
    }
}
