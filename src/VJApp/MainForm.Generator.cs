using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Fundos gerados (Plasma): tipo de ondulação, cores, velocidade, intensidade e escala por clip.</summary>
sealed partial class MainForm
{
    static readonly string[] GenTypes = ["ondas", "nuvens", "aurora", "luzes", "gradiente", "feixes", "arcoiris"];

    static GenParams ToGenParams(GeneratorSettings? g)
    {
        if (g == null) return GenParams.Default; // clips antigos: arco-íris como antes
        Vector4 C(int i)
        {
            var hex = i < g.Colors.Count ? g.Colors[i] : "#000000";
            var c = ParseHex(hex);
            return new Vector4(c.R, c.G, c.B, 1); // ParseHex já devolve 0..1
        }
        return new GenParams
        {
            Color1 = C(0), Color2 = C(1), Color3 = C(2),
            Type = Math.Max(0, Array.IndexOf(GenTypes, g.Type)),
            Speed = Math.Clamp(g.Speed, 0, 5),
            Intensity = Math.Clamp(g.Intensity, 0, 2),
            Scale = Math.Clamp(g.Scale, 0.1f, 5),
            Detail = Math.Clamp(g.Detail, 0, 1),
            Angle = g.Angle * MathF.PI / 180,
        };
    }

    /// <summary>Ajuste do fundo vindo do painel: vale na hora se o clip estiver tocando.</summary>
    void SetGeneratorParams(int layer, int column, JsonNode? node)
    {
        if (layer < 0 || layer >= _comp.Layers.Count || column < 0 || column >= _comp.Columns) return;
        var clip = _comp.Layers[layer].Clips[column];
        if (clip?.Kind != ClipKind.Plasma || node.Deserialize<GeneratorSettings>(JsonRead) is not { } g) return;
        clip.Gen = g;
        if (_comp.ActiveHere(_comp.Layers[layer]) == column && _renderer != null) _renderer.Layers[layer].Gen = ToGenParams(g);
        ScheduleSave();
        // a grade (miniatura com as cores) é atualizada quando a pessoa para de mexer
        _genSendTimer ??= NewGenTimer();
        _genSendTimer.Stop();
        _genSendTimer.Start();
    }

    System.Windows.Forms.Timer? _genSendTimer;

    System.Windows.Forms.Timer NewGenTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += (_, _) => { t.Stop(); SendComposition(withThumbs: false); };
        return t;
    }
}
