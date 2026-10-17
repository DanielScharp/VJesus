using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Layers: inserir, duplicar, remover e mover (quantas a pessoa quiser, até 32).</summary>
sealed partial class MainForm
{
    void OnLayerMessage(string type, JsonNode msg)
    {
        int n = _comp.Layers.Count;
        int at = (int?)msg["layer"] ?? n - 1;
        var map = Enumerable.Range(0, n).ToList(); // novo índice → índice antigo (-1 = layer nova)
        switch (type)
        {
            case "layerAdd":
            {
                // acima da layer indicada (ou no topo)
                if (n >= Composition.MaxLayers) { Send(new { type = "error", message = $"O máximo é {Composition.MaxLayers} layers." }); return; }
                int pos = Math.Clamp(at + 1, 0, n);
                map.Insert(pos, -1);
                ApplyLayerOrder(map, newIndex: pos);
                break;
            }
            case "layerDuplicate":
            {
                if (n >= Composition.MaxLayers || at < 0 || at >= n) return;
                map.Insert(at + 1, -1);
                ApplyLayerOrder(map, newIndex: at + 1, copyFrom: at);
                break;
            }
            case "layerRemove":
                if (n <= 1 || at < 0 || at >= n) return;
                map.RemoveAt(at);
                ApplyLayerOrder(map);
                break;
            case "layerMove":
            {
                int to = at + ((int?)msg["direction"] ?? 0);
                if (at < 0 || at >= n || to < 0 || to >= n) return;
                (map[at], map[to]) = (map[to], map[at]);
                ApplyLayerOrder(map);
                break;
            }
        }
    }

    /// <summary>
    /// Refaz as layers conforme <paramref name="map"/> (posição nova → layer antiga, -1 = nova), levando junto
    /// o que está tocando, efeitos, clips de todas as abas, grupos de layers e fontes dos slices.
    /// </summary>
    void ApplyLayerOrder(List<int> map, int newIndex = -1, int copyFrom = -1)
    {
        _comp.StoreDeck();
        var oldLayers = _comp.Layers;
        var oldSlots = _renderer!.Layers;
        var oldPlayers = _players;

        // layers que saem param de tocar
        for (int i = 0; i < oldLayers.Count; i++)
            if (!map.Contains(i)) ClearLayer(i);

        Layer NewLayer(int pos)
        {
            if (copyFrom >= 0)
            {
                var src = oldLayers[copyFrom];
                var copy = System.Text.Json.JsonSerializer.Deserialize<Layer>(System.Text.Json.JsonSerializer.Serialize(src))!;
                copy.Name = $"{src.Name} cópia";
                copy.Active = -1;
                return copy;
            }
            int k = 1;
            while (oldLayers.Any(l => l.Name == $"Layer {k}")) k++;
            return new Layer { Name = $"Layer {k}", Clips = new Clip?[_comp.Columns] };
        }

        _comp.Layers = [.. map.Select((old, pos) => old >= 0 ? oldLayers[old] : NewLayer(pos))];
        foreach (var d in _comp.Decks)
        {
            var rows = d.Clips;
            d.Clips = [.. map.Select(old =>
                old >= 0 && old < rows.Count ? rows[old]
                : copyFrom >= 0 && copyFrom < rows.Count ? (Clip?[])rows[copyFrom].Clone()
                : new Clip?[d.Columns])];
        }
        _players = [.. map.Select(old => old >= 0 ? oldPlayers[old] : null)];
        _renderer.SetLayers([.. map.Select(old => old >= 0 ? oldSlots[old] : new LayerSlot())]);
        _comp.LoadDeck(_comp.CurrentDeck);

        // grupos de layers e "Só a layer N" nos slices e no LED acompanham a nova numeração
        int Remap(int old) => map.IndexOf(old);
        foreach (var g in _mapping.Groups) g.Layers = [.. g.Layers.Select(Remap).Where(i => i >= 0).Distinct().Order()];
        string? RemapSource(string? s) =>
            s != null && s.StartsWith("layer:") && int.TryParse(s[6..], out var li) ? (Remap(li) is >= 0 and var ni ? $"layer:{ni}" : null) : s;
        foreach (var scr in _mapping.Screens)
        {
            foreach (var sl in scr.Slices) sl.Source = RemapSource(sl.Source);
            if (scr.Led != null)
            {
                scr.Led.Source = RemapSource(scr.Led.Source);
                foreach (var p in scr.Led.Parts) p.Source = RemapSource(p.Source);
            }
        }

        for (int i = 0; i < _comp.Layers.Count; i++) ApplyLayerParams(i);
        RebuildEffectChains(resync: false);
        ApplyMapping();
        _mapping.Save(MappingSetup.DefaultPath);
        SendMapping();
        SendStage();
        CompositionChanged();
    }

    /// <summary>Motor com o mesmo número de layers da composição (ao abrir um projeto).</summary>
    void ResizeEngineLayers()
    {
        if (_renderer == null) return;
        int n = _comp.Layers.Count;
        var slots = _renderer.Layers;
        if (slots.Length == n && _players.Length == n) return;
        _renderer.SetLayers([.. Enumerable.Range(0, n).Select(i => i < slots.Length ? slots[i] : new LayerSlot())]);
        _players = [.. Enumerable.Range(0, n).Select(i => i < _players.Length ? _players[i] : null)];
    }
}
