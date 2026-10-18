using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Propriedades individuais de cada clip da grade (velocidade, ajuste na tela, volume, fim, efeitos).</summary>
sealed partial class MainForm
{
    /// <summary>Efeitos de um clip usam target = 10000 + layer×1000 + coluna (os outros números são layer/master).</summary>
    const int ClipTargetBase = 10000;

    Clip? ClipOfTarget(int target)
    {
        int t = target - ClipTargetBase, l = t / 1000, c = t % 1000;
        return l >= 0 && l < _comp.Layers.Count && c >= 0 && c < _comp.Columns ? _comp.Layers[l].Clips[c] : null;
    }

    List<EffectInstance> ClipChain(int target)
    {
        var clip = ClipOfTarget(target);
        if (clip == null) return [];
        clip.Props ??= new ClipProps();
        return clip.Props.Effects;
    }

    /// <summary>Passa as propriedades do clip para o motor (na hora em que ele toca ou quando mudam).</summary>
    void ApplyClipProps(int layer, Clip clip)
    {
        if (_renderer == null) return;
        var p = clip.Props ?? new ClipProps();
        var slot = _renderer.Layers[layer];
        slot.ClipFit = p.Fit switch { "preencher" => 1, "esticar" => 2, _ => 0 };
        slot.ClipZoom = Math.Clamp(p.Zoom, 0.05f, 10);
        slot.ClipOffsetX = Math.Clamp(p.OffsetX, -2, 2);
        slot.ClipOffsetY = Math.Clamp(p.OffsetY, -2, 2);
        slot.ClipVolume = Math.Clamp(p.Volume, 0, 2);
        slot.ClipEffects = [.. p.Effects.Select(GetRun)];
        if (_players[layer] is { } player)
        {
            player.Speed = Math.Clamp(p.Speed, 0.05f, 8);
            player.HoldAtEnd = p.End == "parar";
        }
    }

    void SetClipProps(int layer, int column, JsonNode? node)
    {
        if (layer < 0 || layer >= _comp.Layers.Count || column < 0 || column >= _comp.Columns) return;
        var clip = _comp.Layers[layer].Clips[column];
        if (clip == null || node.Deserialize<ClipProps>(JsonRead) is not { } p) return;
        p.Effects = clip.Props?.Effects ?? []; // os efeitos mudam pelas mensagens de efeito
        if (node?["name"] is JsonNode nm && ((string?)nm)?.Trim() is { Length: > 0 } name) clip.Name = name;
        clip.Props = p;
        if (_comp.ActiveHere(_comp.Layers[layer]) == column) ApplyClipProps(layer, clip);
        ScheduleSave();
        _clipSendTimer ??= NewClipTimer();
        _clipSendTimer.Stop();
        _clipSendTimer.Start();
    }

    System.Windows.Forms.Timer? _clipSendTimer;

    System.Windows.Forms.Timer NewClipTimer()
    {
        var t = new System.Windows.Forms.Timer { Interval = 300 };
        t.Tick += (_, _) => { t.Stop(); SendComposition(withThumbs: false); };
        return t;
    }

    /// <summary>Depois de mudar efeitos de um clip, atualiza a cadeia no motor se ele estiver tocando.</summary>
    void RefreshPlayingClipEffects()
    {
        if (_renderer == null) return;
        for (int i = 0; i < _comp.Layers.Count; i++)
            _renderer.Layers[i].ClipEffects = _comp.Layers[i].PlayingClip?.Props is { } p ? [.. p.Effects.Select(GetRun)] : [];
    }
}
