using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VJApp;

/// <summary>
/// Passador de slides (apresentador sem fio): ele age como um teclado e manda Page Down / Page Up
/// (e B ou ponto para a tela preta). Com uma apresentação tocando, essas teclas passam o slide mesmo
/// quando o foco está no telão ou na tela de retorno; com a opção "em segundo plano", até com outro
/// programa na frente. Na janela principal quem cuida das teclas é a própria interface (aba Clips).
/// </summary>
sealed partial class MainForm
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const int VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_B = 0x42, VK_OEM_PERIOD = 0xBE;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);

    IntPtr _clickerHook;
    LowLevelKeyboardProc? _clickerProc;
    bool _clickerGlobal;
    static readonly uint OwnPid = (uint)Environment.ProcessId;
    static string ClickerPath => Path.Combine(AppDataDir, "passador.json");

    /// <summary>Layers de apresentação com a tela preta ligada (botão do passador).</summary>
    readonly HashSet<int> _presBlack = [];

    void LoadClicker()
    {
        try { if (File.Exists(ClickerPath)) _clickerGlobal = JsonDocument.Parse(File.ReadAllText(ClickerPath)).RootElement.GetProperty("global").GetBoolean(); }
        catch { /* arquivo ruim: padrão */ }
    }

    void SetClickerGlobal(bool on)
    {
        _clickerGlobal = on;
        try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(ClickerPath, JsonSerializer.Serialize(new { global = on })); } catch { }
        SendPresentations();
    }

    /// <summary>Liga a escuta das teclas só enquanto alguma apresentação está tocando.</summary>
    void UpdateClickerHook(bool presentationPlaying)
    {
        if (presentationPlaying && _clickerHook == IntPtr.Zero)
        {
            _clickerProc ??= ClickerHook;
            _clickerHook = SetWindowsHookEx(WH_KEYBOARD_LL, _clickerProc, GetModuleHandle(null), 0);
        }
        else if (!presentationPlaying && _clickerHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_clickerHook);
            _clickerHook = IntPtr.Zero;
        }
    }

    IntPtr ClickerHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk is VK_NEXT or VK_PRIOR or VK_B or VK_OEM_PERIOD && ClickerTarget(vk) is { } action)
            {
                BeginInvoke(action);
                return 1; // a tecla foi do passador: não vai para a outra janela
            }
        }
        return CallNextHookEx(_clickerHook, nCode, wParam, lParam);
    }

    /// <summary>O que a tecla faz agora (null = deixa a tecla seguir normalmente).</summary>
    Action? ClickerTarget(int vk)
    {
        // com Ctrl/Alt/Win é atalho de outra coisa
        if (GetKeyState(0x11) < 0 || GetKeyState(0x12) < 0 || GetKeyState(0x5B) < 0 || GetKeyState(0x5C) < 0) return null;
        if (PresentationLayer() < 0) return null;
        var fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint pid);
        bool own = pid == OwnPid;
        if (own && fg == Handle) return null; // janela principal: a interface já trata (aba Clips)
        if (!own && !_clickerGlobal) return null;
        // B e ponto só nas janelas do VJesus (em outro programa seriam letras digitadas)
        if (!own && vk is VK_B or VK_OEM_PERIOD) return null;
        return vk switch
        {
            VK_NEXT => () => PresentationStep(-1, 1),
            VK_PRIOR => () => PresentationStep(-1, -1),
            _ => () => PresentationBlack(-1),
        };
    }

    /// <summary>Tela preta do passador: esconde a apresentação (de novo, ou passando o slide, volta).</summary>
    void PresentationBlack(int layer)
    {
        layer = PresentationLayer(layer);
        if (layer < 0) return;
        if (!_presBlack.Remove(layer)) _presBlack.Add(layer);
        ApplyLayerParams(layer);
        SendPresentations();
    }
}
