using System.Runtime.InteropServices;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>
/// Uso geral do computador (todos os programas, não só o VJesus), medido uma vez por segundo numa thread própria:
/// processador (CPU), memória RAM e memória de vídeo (VRAM). Placa de vídeo dedicada: a memória da placa;
/// vídeo integrado (Intel/AMD no processador): a parte da RAM que o Windows reserva para o vídeo.
/// </summary>
sealed class SystemMonitor : IDisposable
{
    public volatile float Cpu = -1, Ram = -1, Vram = -1;
    /// <summary>Texto para a dica: nome da placa e quanto está em uso.</summary>
    public volatile string VramInfo = "";

    readonly Thread _thread;
    volatile bool _running = true;

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx m);
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    // contadores do Windows pelo nome em inglês (em Windows em português o nome da categoria é traduzido)
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern int PdhAddEnglishCounter(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] static extern int PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhValue value);
    [DllImport("pdh.dll")] static extern int PdhCloseQuery(IntPtr query);
    [StructLayout(LayoutKind.Explicit)] struct PdhValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Double; }

    public SystemMonitor()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Uso do computador", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    void Run()
    {
        long lastIdle = 0, lastTotal = 0;
        // placa de vídeo usada: a com mais memória dedicada (a mesma que o motor escolhe)
        string? instance = null; ulong vramTotal = 0; bool dedicated = false; string gpuName = "";
        try
        {
            using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
            for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
            {
                using (adapter)
                {
                    var d = adapter.Description1;
                    if ((d.Flags & AdapterFlags.Software) != 0) continue;
                    bool isDedicated = (ulong)d.DedicatedVideoMemory >= 256UL * 1024 * 1024;
                    ulong total = isDedicated ? (ulong)d.DedicatedVideoMemory : (ulong)d.SharedSystemMemory;
                    if (instance == null || (isDedicated && !dedicated) || (isDedicated == dedicated && total > vramTotal))
                    {
                        instance = $"luid_0x{(uint)d.Luid.HighPart:x8}_0x{d.Luid.LowPart:x8}_phys_0";
                        vramTotal = total; dedicated = isDedicated; gpuName = d.Description;
                    }
                }
            }
        }
        catch { }

        IntPtr query = IntPtr.Zero, vramCounter = IntPtr.Zero;
        if (instance != null && PdhOpenQuery(null, IntPtr.Zero, out query) == 0)
        {
            if (PdhAddEnglishCounter(query, $@"\GPU Adapter Memory({instance})\{(dedicated ? "Dedicated Usage" : "Shared Usage")}", IntPtr.Zero, out vramCounter) != 0) vramCounter = IntPtr.Zero;
        }

        while (_running)
        {
            try
            {
                if (GetSystemTimes(out long idle, out long kernel, out long user))
                {
                    long total = kernel + user; // o tempo de kernel já inclui o ocioso
                    if (lastTotal > 0 && total > lastTotal)
                        Cpu = Math.Clamp(100f * (1 - (float)(idle - lastIdle) / (total - lastTotal)), 0, 100);
                    lastIdle = idle; lastTotal = total;
                }
                var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref m)) Ram = m.MemoryLoad;
                if (vramCounter != IntPtr.Zero && vramTotal > 0 && PdhCollectQueryData(query) == 0
                    && PdhGetFormattedCounterValue(vramCounter, 0x200, out _, out var v) == 0)
                {
                    double used = v.Double;
                    Vram = (float)Math.Clamp(100 * used / vramTotal, 0, 100);
                    VramInfo = $"{gpuName}: {used / 1073741824:0.0} de {vramTotal / 1073741824.0:0.0} GB ({(dedicated ? "memória da placa" : "vídeo integrado, memória compartilhada")})";
                }
            }
            catch { /* contador indisponível: mostra só o que der */ }
            Thread.Sleep(1000);
        }
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }

    public void Dispose() => _running = false;
}
