using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// "No fim: ir para…": quando um vídeo que não repete chega na saída, o VJesus toca sozinho o próximo
/// conteúdo programado (outro clip de qualquer aba e layer, a próxima coluna, ou limpa a layer).
/// Alguns segundos antes do fim o vídeo seguinte já é aberto e decodificado, para entrar na hora certa.
/// </summary>
sealed partial class MainForm
{
    /// <summary>Layers cujo fim já disparou a próxima exibição (para não disparar de novo).</summary>
    readonly HashSet<int> _endFired = [];

    /// <summary>Vídeo seguinte já aberto, por layer de origem.</summary>
    readonly Dictionary<int, (Clip Clip, VideoPlayer Player)> _preloads = [];

    /// <summary>Quanto tempo antes do fim o vídeo seguinte começa a ser preparado.</summary>
    const double PreloadSeconds = 3;

    /// <summary>
    /// Folga para o disparo: a conferência roda a cada 100 ms, então dispara um pouco antes da saída
    /// para o próximo entrar no último quadro, sem esperar nada parado.
    /// </summary>
    const double EndLead = 0.1;

    sealed record NextTarget(string DeckId, int Layer, int Column, Clip Clip);

    /// <summary>Chamado a cada 100 ms: confere quem chegou ao fim (e prepara quem está perto).</summary>
    void CheckClipEnds()
    {
        for (int layer = 0; layer < _comp.Layers.Count && layer < _players.Length; layer++)
        {
            var player = _players[layer];
            var clip = _comp.Layers[layer].PlayingClip;
            if (player == null || player.IsStill || clip?.Props is not { End: "seguir" } props || player.Duration <= 0)
            {
                _endFired.Remove(layer);
                DropPreload(layer);
                continue;
            }
            double end = player.OutPoint, pos = player.Position;
            // voltou o vídeo (stop, seek): pode disparar de novo quando chegar ao fim
            if (pos < end - 0.5) _endFired.Remove(layer);
            if (pos < end - PreloadSeconds - 0.5) { DropPreload(layer); continue; }
            if (!player.Playing || _endFired.Contains(layer)) continue;

            var next = props.Next ?? new ClipNext();
            if (pos < end - EndLead)
            {
                Preload(layer, ResolveNext(layer, next));
                continue;
            }
            _endFired.Add(layer);
            FollowClipEnd(layer, next);
        }
    }

    /// <summary>Para onde vai depois do fim (null = limpar a layer ou o clip programado sumiu).</summary>
    NextTarget? ResolveNext(int layer, ClipNext next)
    {
        var current = _comp.Layers[layer];
        switch (next.Action)
        {
            case "limpar":
                return null;
            case "proxima":
            {
                // próxima coluna com clip, na mesma aba e layer de onde veio o vídeo
                var deck = _comp.Decks.FirstOrDefault(d => d.Id == current.ActiveDeck) ?? _comp.Current;
                if (deck == _comp.Current) _comp.StoreDeck();
                var row = layer < deck.Clips.Count ? deck.Clips[layer] : [];
                int col = current.Active + 1;
                while (col < row.Length && row[col] == null) col++;
                return col < row.Length ? new NextTarget(deck.Id, layer, col, row[col]!) : null;
            }
            default:
            {
                var deck = (next.DeckId != null ? _comp.Decks.FirstOrDefault(d => d.Id == next.DeckId) : null) ?? _comp.Current;
                if (deck == _comp.Current) _comp.StoreDeck();
                var target = next.Layer >= 0 && next.Layer < deck.Clips.Count && next.Layer < _comp.Layers.Count
                    && next.Column >= 0 && next.Column < deck.Clips[next.Layer].Length
                    ? deck.Clips[next.Layer][next.Column] : null;
                return target == null ? null : new NextTarget(deck.Id, next.Layer, next.Column, target);
            }
        }
    }

    /// <summary>Abre o vídeo seguinte antes da hora: quando o atual acabar, ele já tem imagem.</summary>
    void Preload(int layer, NextTarget? target)
    {
        if (target?.Clip is not { Kind: ClipKind.Video, Path: { } path } clip) { DropPreload(layer); return; }
        if (_preloads.TryGetValue(layer, out var p) && p.Clip == clip) return;
        DropPreload(layer);
        try { _preloads[layer] = (clip, new VideoPlayer(path)); }
        catch { /* abre normalmente na hora; o erro aparece lá */ }
    }

    void DropPreload(int layer)
    {
        if (!_preloads.Remove(layer, out var p)) return;
        Task.Run(p.Player.Dispose);
    }

    /// <summary>O vídeo já preparado para este clip, se houver (quem pega passa a ser o dono).</summary>
    VideoPlayer? TakePreloaded(Clip clip)
    {
        foreach (var (layer, p) in _preloads)
        {
            if (p.Clip != clip) continue;
            _preloads.Remove(layer);
            return p.Player;
        }
        return null;
    }

    void FollowClipEnd(int layer, ClipNext next)
    {
        var current = _comp.Layers[layer];
        var target = ResolveNext(layer, next);
        if (target == null)
        {
            if (next.Action is "limpar" or "proxima") ClearLayer(layer);
            else Send(new { type = "error", message = $"\"{current.PlayingClip?.Name}\" terminou, mas o clip programado para depois não está mais na grade." });
            DropPreload(layer);
            SendComposition(withThumbs: false);
            return;
        }
        // as duas trocas entram juntas: o vídeo que acabou sai enquanto o próximo entra (sem tela preta no meio)
        BeginTriggerGroup();
        Trigger(target.Layer, target.Column, target.Clip, target.DeckId);
        if (target.Layer != layer) ClearLayer(layer);
        EndTriggerGroup();
        DropPreload(layer); // se não foi usado (ex.: o próximo não é vídeo)
        SendComposition(withThumbs: false);
    }
}
