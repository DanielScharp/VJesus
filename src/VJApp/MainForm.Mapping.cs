using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Mapeamento: janelas de saída por tela, slices/máscaras e preview da composição.</summary>
sealed partial class MainForm
{
    static readonly JsonSerializerOptions JsonRead = new() { PropertyNameCaseInsensitive = true };

    MappingSetup _mapping = MappingSetup.LoadOrDefault(MappingSetup.DefaultPath);
    readonly Dictionary<string, OutputWindow> _windows = [];
    readonly Dictionary<string, (int Display, bool Fullscreen)> _placement = [];
    System.Windows.Forms.Timer? _mappingSaveTimer;

    void SetMapping(JsonNode node)
    {
        var m = node.Deserialize<MappingSetup>(JsonRead);
        if (m == null || m.Screens.Count == 0) return;
        _mapping = m;
        ApplyMapping();

        _mappingSaveTimer ??= CreateMappingSaveTimer();
        _mappingSaveTimer.Stop();
        _mappingSaveTimer.Start();
    }

    System.Windows.Forms.Timer CreateMappingSaveTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 500 };
        t.Tick += (_, _) => { t.Stop(); _mapping.Save(MappingSetup.DefaultPath); };
        return t;
    }

    /// <summary>Abre/fecha/posiciona as janelas de saída e manda as malhas para o motor.</summary>
    void ApplyMapping()
    {
        if (_renderer == null) return;

        // fecha janelas de telas removidas
        foreach (var id in _windows.Keys.Where(id => _mapping.Screens.All(s => s.Id != id)).ToList())
        {
            _windows[id].AllowClose = true;
            _windows[id].Close();
            _windows.Remove(id);
            _placement.Remove(id);
        }

        var specs = new List<OutputSpec>();
        int index = 0;
        foreach (var screen in _mapping.Screens)
        {
            if (!_windows.TryGetValue(screen.Id, out var win))
            {
                win = new OutputWindow();
                // janelas novas ficam em cascata para não ficarem uma em cima da outra
                win.Location = new Point(80 + index * 40, 80 + index * 40);
                win.Show();
                var w = win;
                w.ClientSizeChanged += (_, _) => _renderer?.ResizeOutput(w.Handle, w.ClientSize.Width, w.ClientSize.Height);
                _windows[screen.Id] = win;
            }
            win.Text = $"VJApp - {screen.Name}";

            var place = (screen.Display, screen.Fullscreen);
            if (!_placement.TryGetValue(screen.Id, out var last) || last != place)
            {
                // a primeira vez em modo janela mantém a posição em cascata
                if (_placement.ContainsKey(screen.Id) || screen.Fullscreen) win.PlaceOn(screen.Display, screen.Fullscreen);
                _placement[screen.Id] = place;
            }

            specs.Add(new OutputSpec(win.Handle, win.ClientSize.Width, win.ClientSize.Height,
                MappingMesh.BuildSlices(screen), MappingMesh.BuildMasks(screen)));
            index++;
        }
        _renderer.SetOutputs(specs);
    }

    void CloseOutputWindows()
    {
        foreach (var w in _windows.Values) { w.AllowClose = true; w.Close(); }
        _windows.Clear();
    }

    void SendMapping() => Send(new { type = "mapping", mapping = _mapping });

    /// <summary>Marca "codificando" de um canal de preview (para pular quadros quando a CPU está ocupada).</summary>
    sealed class EncodeGate { public int Busy; }
    readonly EncodeGate _mainGate = new(), _stageGate = new();

    /// <summary>Preview do telão: JPEG para a UI.</summary>
    void OnPreview(byte[] bgra, int width, int height, int pitch) =>
        EncodePreview(_mainGate, bgra, width, height, pitch, data => Send(new { type = "preview", data }));

    /// <summary>Imagem do item "Telão" da tela de retorno: para a janela de retorno e o monitor RETORNO da UI.</summary>
    void OnStagePreview(byte[] bgra, int width, int height, int pitch) =>
        EncodePreview(_stageGate, bgra, width, height, pitch, data =>
        {
            var payload = new { type = "stagePreview", data };
            Send(payload);
            BeginInvoke(() => _stageWindow?.Post(JsonSerializer.Serialize(payload, Json)));
        });

    /// <summary>Converte BGRA em JPEG fora da thread de render (pula o quadro se ainda está codificando o anterior).</summary>
    static void EncodePreview(EncodeGate gate, byte[] bgra, int width, int height, int pitch, Action<string> deliver)
    {
        if (Interlocked.Exchange(ref gate.Busy, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                using var bmp = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                for (int y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(bgra, y * pitch, data.Scan0 + y * data.Stride, width * 4);
                bmp.UnlockBits(data);

                using var ms = new MemoryStream();
                var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ps = new EncoderParameters(1);
                ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                bmp.Save(ms, jpeg, ps);
                deliver("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()));
            }
            finally
            {
                Interlocked.Exchange(ref gate.Busy, 0);
            }
        });
    }
}
