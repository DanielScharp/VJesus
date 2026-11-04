using VJApp.Model;

namespace VJApp;

/// <summary>
/// "No fim: ir para…": quando um vídeo que não repete chega na saída, o VJesus toca sozinho o próximo
/// conteúdo programado (outro clip de qualquer aba e layer, a próxima coluna, ou limpa a layer).
/// </summary>
sealed partial class MainForm
{
    /// <summary>Layers cujo fim já disparou a próxima exibição (para não disparar de novo).</summary>
    readonly HashSet<int> _endFired = [];

    /// <summary>Chamado a cada 100 ms: confere quem chegou ao fim.</summary>
    void CheckClipEnds()
    {
        for (int layer = 0; layer < _comp.Layers.Count && layer < _players.Length; layer++)
        {
            var player = _players[layer];
            var clip = _comp.Layers[layer].PlayingClip;
            if (player == null || clip?.Props is not { End: "seguir" } props || player.Duration <= 0) { _endFired.Remove(layer); continue; }
            double end = player.OutPoint, pos = player.Position;
            // voltou o vídeo (stop, seek): pode disparar de novo quando chegar ao fim
            if (pos < end - 0.5) { _endFired.Remove(layer); continue; }
            if (!player.Playing || pos < end - 0.05 || !_endFired.Add(layer)) continue;
            FollowClipEnd(layer, props.Next ?? new ClipNext());
        }
    }

    void FollowClipEnd(int layer, ClipNext next)
    {
        var current = _comp.Layers[layer];
        _comp.StoreDeck();
        switch (next.Action)
        {
            case "limpar":
                ClearLayer(layer);
                break;
            case "proxima":
            {
                // próxima coluna com clip, na mesma aba e layer de onde veio o vídeo
                var deck = _comp.Decks.FirstOrDefault(d => d.Id == current.ActiveDeck) ?? _comp.Current;
                var row = layer < deck.Clips.Count ? deck.Clips[layer] : [];
                int col = current.Active + 1;
                while (col < row.Length && row[col] == null) col++;
                if (col < row.Length) Trigger(layer, col, row[col], deck.Id);
                else ClearLayer(layer);
                break;
            }
            default:
            {
                var deck = (next.DeckId != null ? _comp.Decks.FirstOrDefault(d => d.Id == next.DeckId) : null) ?? _comp.Current;
                var target = next.Layer >= 0 && next.Layer < deck.Clips.Count && next.Column >= 0 && next.Column < deck.Clips[next.Layer].Length
                    ? deck.Clips[next.Layer][next.Column] : null;
                if (target == null || next.Layer >= _comp.Layers.Count)
                {
                    Send(new { type = "error", message = $"\"{current.PlayingClip?.Name}\" terminou, mas o clip programado para depois não está mais na grade." });
                    return;
                }
                // em outra layer: o vídeo que acabou sai (com a transição da layer dele)
                if (next.Layer != layer) ClearLayer(layer);
                Trigger(next.Layer, next.Column, target, deck.Id);
                break;
            }
        }
        SendComposition(withThumbs: false);
    }
}
