namespace VJApp.Engine;

/// <summary>
/// Grupos de layers: composições à parte (ex.: só a layer 1 para as laterais do LED, layers 2 e 3 para o
/// centro). Só são montados os grupos que algum slice usa.
/// </summary>
partial class Renderer
{
    volatile int[][] _groups = [];
    readonly Dictionary<int, RenderTexture?[]> _groupComp = [];
    readonly Dictionary<int, RenderTexture> _groupResult = [];

    /// <summary>Layers (índices) de cada grupo, na ordem do mapeamento.</summary>
    public void SetGroups(int[][] groups) => _groups = groups;

    Dictionary<int, RenderTexture> ComposeGroups(float time)
    {
        _groupResult.Clear();
        var groups = _groups;
        foreach (var o in _outputs)
            foreach (var (source, _, _) in o.SliceBatches)
            {
                if (source < 0 || source >= groups.Length || _groupResult.ContainsKey(source)) continue;
                if (!_groupComp.TryGetValue(source, out var pair))
                    _groupComp[source] = pair = [new RenderTexture(_device!, CompWidth, CompHeight), new RenderTexture(_device!, CompWidth, CompHeight)];
                var layers = groups[source];
                int r = Compose(time, pair, s => layers.Contains(Array.IndexOf(Layers, s)), main: false);
                if (ShowTestCard)
                {
                    var lp = new LayerParams { Opacity = 1, SourceKind = (float)SourceKind.TestCard };
                    _ctx!.UpdateSubresource(in lp, _layerCb!);
                    r = 1 - r;
                    Pass(_psSource!, pair[r]!.Rtv, null);
                }
                r = ApplyAttacks(pair, r, time);
                _groupResult[source] = pair[r]!;
            }
        // libera texturas de grupos que deixaram de existir
        foreach (var k in _groupComp.Keys.Where(k => k >= groups.Length).ToList())
        {
            foreach (var t in _groupComp[k]) t?.Dispose();
            _groupComp.Remove(k);
        }
        return _groupResult;
    }

    void DisposeGroups()
    {
        foreach (var pair in _groupComp.Values) foreach (var t in pair) t?.Dispose();
        _groupComp.Clear();
    }
}
