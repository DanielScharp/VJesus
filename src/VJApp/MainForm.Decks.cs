using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>Grade de clips: colunas (adicionar/remover) e abas de clips (como os decks do Resolume).</summary>
sealed partial class MainForm
{
    void OnGridMessage(string type, JsonNode msg)
    {
        int Int(string key, int def = 0) => (int?)msg[key] ?? def;
        switch (type)
        {
            case "columnAdd":
            {
                // nova coluna no fim ou depois de "after"
                int at = Math.Clamp(Int("after", _comp.Columns - 1) + 1, 0, _comp.Columns);
                foreach (var l in _comp.Layers)
                {
                    l.Clips = [.. l.Clips.Take(at), null, .. l.Clips.Skip(at)];
                    if (_comp.ActiveHere(l) >= at) l.Active++;
                }
                _comp.ColumnNames.Insert(Math.Min(at, _comp.ColumnNames.Count), null);
                _comp.Columns++;
                break;
            }
            case "columnRemove":
            {
                int col = Int("column", -1);
                if (_comp.Columns <= 1 || col < 0 || col >= _comp.Columns) return;
                for (int i = 0; i < _comp.Layers.Count; i++)
                {
                    var l = _comp.Layers[i];
                    int here = _comp.ActiveHere(l);
                    if (here == col) ClearLayer(i);
                    else if (here > col) l.Active--;
                    l.Clips = [.. l.Clips.Take(col), .. l.Clips.Skip(col + 1)];
                }
                if (col < _comp.ColumnNames.Count) _comp.ColumnNames.RemoveAt(col);
                _comp.Columns--;
                break;
            }
            case "columnRename":
            {
                int col = Int("column", -1);
                if (col < 0 || col >= _comp.Columns) return;
                while (_comp.ColumnNames.Count < _comp.Columns) _comp.ColumnNames.Add(null);
                var name = ((string?)msg["name"])?.Trim();
                _comp.ColumnNames[col] = string.IsNullOrEmpty(name) ? null : name;
                break;
            }
            case "deckAdd":
            {
                _comp.StoreDeck();
                var name = ((string?)msg["name"])?.Trim();
                var deck = new Deck
                {
                    Name = string.IsNullOrEmpty(name) ? $"Aba {_comp.Decks.Count + 1}" : name,
                    Columns = Composition.DefaultColumns,
                    Clips = [.. _comp.Layers.Select(_ => new Clip?[Composition.DefaultColumns])],
                };
                _comp.Decks.Add(deck);
                _comp.LoadDeck(_comp.Decks.Count - 1);
                break;
            }
            case "deckSelect":
                _comp.StoreDeck();
                _comp.LoadDeck(Int("index"));
                break;
            case "deckRename":
            {
                int i = Int("index", -1);
                var name = ((string?)msg["name"])?.Trim();
                if (i < 0 || i >= _comp.Decks.Count || string.IsNullOrEmpty(name)) return;
                _comp.Decks[i].Name = name;
                break;
            }
            case "deckRemove":
            {
                int i = Int("index", -1);
                if (_comp.Decks.Count <= 1 || i < 0 || i >= _comp.Decks.Count) return;
                _comp.StoreDeck();
                // clips tocando dessa aba param
                var id = _comp.Decks[i].Id;
                for (int k = 0; k < _comp.Layers.Count; k++)
                    if (_comp.Layers[k].ActiveDeck == id) ClearLayer(k);
                int current = _comp.CurrentDeck;
                _comp.Decks.RemoveAt(i);
                _comp.LoadDeck(current > i || current >= _comp.Decks.Count ? Math.Max(0, current - 1) : current);
                break;
            }
            case "deckMove":
            {
                int i = Int("index", -1), d = Int("direction");
                int j = i + d;
                if (i < 0 || j < 0 || i >= _comp.Decks.Count || j >= _comp.Decks.Count) return;
                _comp.StoreDeck();
                var cur = _comp.Current;
                (_comp.Decks[i], _comp.Decks[j]) = (_comp.Decks[j], _comp.Decks[i]);
                _comp.CurrentDeck = _comp.Decks.IndexOf(cur);
                break;
            }
        }
        CompositionChanged();
    }
}
