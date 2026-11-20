using VJApp.Model;

namespace VJApp;

/// <summary>
/// Clips que perderam a referência (o arquivo foi apagado ou a pasta sumiu) saem do grid sozinhos,
/// em todas as abas. Se a unidade inteira está fora do ar (HD externo ou pendrive desligado, Google Drive
/// desconectado), nada é tirado: quando a unidade voltar, os clips voltam a funcionar.
/// </summary>
sealed partial class MainForm
{
    /// <summary>Tira do grid os clips sem arquivo. Devolve quantos saíram.</summary>
    int RemoveMissingClips(bool notify)
    {
        _comp.StoreDeck();
        var removed = new List<string>();
        foreach (var deck in _comp.Decks)
        {
            for (int l = 0; l < deck.Clips.Count; l++)
            {
                var row = deck.Clips[l];
                for (int c = 0; c < row.Length; c++)
                {
                    if (row[c] is not { } clip || !LostReference(clip)) continue;
                    // o que está tocando agora fica (o vídeo já está aberto); sai na próxima vez
                    if (l < _comp.Layers.Count && _comp.Layers[l].ActiveDeck == deck.Id && _comp.Layers[l].Active == c) continue;
                    row[c] = null;
                    removed.Add(clip.Name);
                }
            }
        }
        if (removed.Count == 0) return 0;
        _comp.LoadDeck(_comp.CurrentDeck);
        _comp.Save(Composition.DefaultPath);
        SendComposition();
        if (notify)
            Send(new { type = "notice", message = $"{removed.Count} clip(s) sem arquivo saíram do grid: {string.Join(", ", removed.Distinct().Take(5))}{(removed.Count > 5 ? "…" : "")}" });
        return removed.Count;
    }

    /// <summary>O arquivo do clip não existe mais, e a unidade onde ele estava está ligada.</summary>
    static bool LostReference(Clip clip)
    {
        if (string.IsNullOrWhiteSpace(clip.Path)) return false;
        bool exists = clip.Kind switch
        {
            ClipKind.Video => File.Exists(clip.Path),
            ClipKind.Presentation => Directory.Exists(clip.Path),
            _ => true, // letras, gerados, câmeras, NDI...: não dependem de arquivo
        };
        if (exists) return false;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(clip.Path));
            return !string.IsNullOrEmpty(root) && Directory.Exists(root);
        }
        catch { return false; }
    }
}
