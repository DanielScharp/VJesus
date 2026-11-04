using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Desempenho dos vídeos: escolher onde os vídeos são decodificados (placa de vídeo ou processador)
/// e testar qual das duas formas anda melhor neste computador. A escolha fica salva no PC, não no projeto.
/// </summary>
sealed partial class MainForm
{
    sealed class PerformanceSettings
    {
        /// <summary>"auto", "gpu" ou "cpu".</summary>
        public string Decode { get; set; } = "auto";
    }

    static string PerformancePath => Path.Combine(AppDataDir, "desempenho.json");
    PerformanceSettings _perf = new();
    object? _perfTest;
    bool _perfTesting;

    void LoadPerformance()
    {
        try
        {
            if (File.Exists(PerformancePath))
                _perf = JsonSerializer.Deserialize<PerformanceSettings>(File.ReadAllText(PerformancePath)) ?? new();
        }
        catch { _perf = new(); }
        if (_perf.Decode is not ("auto" or "gpu" or "cpu")) _perf.Decode = "auto";
        VideoPlayer.DecodeMode = _perf.Decode;
    }

    void OnPerformanceMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "performanceGet":
                SendPerformance();
                break;
            case "performanceSet":
                var mode = (string?)msg["decode"];
                if (mode is not ("auto" or "gpu" or "cpu")) return;
                _perf.Decode = mode;
                VideoPlayer.DecodeMode = mode;
                try { File.WriteAllText(PerformancePath, JsonSerializer.Serialize(_perf)); } catch { }
                SendPerformance();
                break;
            case "performanceTest":
                if (!_perfTesting) _ = RunPerformanceTestAsync();
                break;
        }
    }

    /// <summary>Escolha atual, quem está na placa agora e o resultado do último teste.</summary>
    void SendPerformance()
    {
        var layers = new List<object>();
        for (int l = 0; l < _players.Length && l < _comp.Layers.Count; l++)
            if (_players[l] is { IsLive: false, IsStill: false } p)
                layers.Add(new { layer = _comp.Layers[l].Name, clip = Path.GetFileName(p.FilePath), codec = p.CodecName, size = $"{p.SourceWidth}×{p.SourceHeight}", gpu = p.HardwareDecode });
        Send(new { type = "performance", decode = _perf.Decode, testing = _perfTesting, layers, test = _perfTest });
    }

    /// <summary>
    /// Toca os vídeos mais pesados do projeto, sem aparecer no telão, primeiro pelo processador e depois
    /// pela placa de vídeo, e compara: quantos quadros chegaram na hora e quanto processador foi gasto.
    /// </summary>
    async Task RunPerformanceTestAsync()
    {
        var files = AllClips(_comp)
            .Where(c => c is { Kind: ClipKind.Video, Path: { } p } && File.Exists(p) && !IsStillFile(p))
            .Select(c => c!.Path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => new FileInfo(p).Length)
            .Take(3)
            .ToList();
        if (files.Count == 0)
        {
            _perfTest = new { error = "Coloque pelo menos um vídeo na grade para testar." };
            SendPerformance();
            return;
        }
        _perfTesting = true;
        _perfTest = null;
        SendPerformance();
        try
        {
            var cpu = await Task.Run(() => Measure(files, "cpu"));
            var gpu = await Task.Run(() => Measure(files, "gpu"));
            string suggestion, why;
            if (gpu.GpuCount == 0)
            {
                suggestion = "cpu";
                why = "A placa de vídeo deste computador não decodifica esses vídeos; fica no processador.";
            }
            else if (gpu.Smooth >= cpu.Smooth - 3 && (gpu.Cpu < cpu.Cpu * 0.9 || gpu.Smooth > cpu.Smooth + 3))
            {
                suggestion = "auto";
                why = gpu.Smooth > cpu.Smooth + 3
                    ? "Pela placa de vídeo os vídeos andaram mais lisos."
                    : "Pela placa de vídeo os vídeos andaram iguais gastando menos processador.";
            }
            else if (cpu.Smooth > gpu.Smooth + 3)
            {
                suggestion = "cpu";
                why = "Neste computador o processador deu conta melhor que a placa de vídeo.";
            }
            else
            {
                suggestion = "auto";
                why = "As duas formas ficaram parecidas; o automático usa a placa e deixa o processador livre.";
            }
            _perfTest = new
            {
                files = files.Select(Path.GetFileName),
                cpu = new { smooth = Math.Round(cpu.Smooth), usage = Math.Round(cpu.Cpu) },
                gpu = new { smooth = Math.Round(gpu.Smooth), usage = Math.Round(gpu.Cpu), count = gpu.GpuCount, total = files.Count },
                suggestion,
                why,
            };
        }
        catch (Exception ex)
        {
            _perfTest = new { error = "O teste falhou: " + ex.Message };
        }
        finally
        {
            _perfTesting = false;
            SendPerformance();
        }
    }

    static bool IsStillFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tif" or ".tiff";

    /// <summary>Toca os arquivos juntos por alguns segundos num modo e mede a fluidez (%) e o processador (%).</summary>
    static (double Smooth, double Cpu, int GpuCount) Measure(List<string> files, string mode)
    {
        const double Seconds = 5;
        var players = new List<VideoPlayer>();
        try
        {
            foreach (var f in files)
            {
                try { players.Add(new VideoPlayer(f, decodeMode: mode)); } catch { }
            }
            if (players.Count == 0) return (0, 0, 0);
            // espera os primeiros quadros (abrir não conta no teste)
            var shown = players.Select(p => (VideoFrame?)null).ToArray();
            var warm = Stopwatch.StartNew();
            while (warm.Elapsed.TotalSeconds < 1.5)
            {
                for (int i = 0; i < players.Count; i++) shown[i] = players[i].TakeFrame(shown[i]) ?? shown[i];
                Thread.Sleep(5);
            }
            var proc = Process.GetCurrentProcess();
            double cpu0 = proc.TotalProcessorTime.TotalSeconds;
            var counts = new int[players.Count];
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < Seconds)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    var f = players[i].TakeFrame(shown[i]);
                    if (f != null) { shown[i] = f; counts[i]++; }
                }
                Thread.Sleep(4);
            }
            proc.Refresh();
            double cpu = (proc.TotalProcessorTime.TotalSeconds - cpu0) / sw.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
            // fluidez: quadros mostrados ÷ quadros que o vídeo tem nesse tempo (no máximo 60 por segundo, como a tela)
            double smooth = players.Select((p, i) => Math.Min(1, counts[i] / (Math.Min(p.FrameRate, 60) * sw.Elapsed.TotalSeconds))).Average() * 100;
            return (smooth, cpu, players.Count(p => p.HardwareDecode));
        }
        finally
        {
            foreach (var p in players) p.Dispose();
        }
    }
}
