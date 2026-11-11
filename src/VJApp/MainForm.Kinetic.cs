using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Animação de letra (tipografia cinética): o editor da interface desenha e grava o vídeo (MP4) e manda
/// os pedaços para cá. O arquivo fica em %AppData%\VJesus\animacoes, com o projeto ao lado
/// ("nome.mp4.vjanim.json") para poder editar de novo, e entra no grid como um vídeo comum.
/// </summary>
sealed partial class MainForm
{
    static string KineticDir => Path.Combine(AppDataDir, "animacoes");
    const string KineticProjectExt = ".vjanim.json";

    sealed class KineticWrite(FileStream file, string path, int layer, int column, string name, string project, string? deckId)
    {
        public FileStream File { get; } = file;
        public string Path { get; } = path;
        public int Layer { get; } = layer;
        public int Column { get; } = column;
        public string Name { get; } = name;
        public string Project { get; } = project;
        public string? DeckId { get; } = deckId;
    }

    readonly Dictionary<string, KineticWrite> _kineticWrites = [];

    void OnKineticMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "kineticBegin":
            {
                var id = (string?)msg["id"];
                if (id == null) break;
                Directory.CreateDirectory(KineticDir);
                string name = ((string?)msg["name"])?.Trim() is { Length: > 0 } n ? n : "Animação";
                string safe = string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
                if (safe.Length > 60) safe = safe[..60];
                string path = Path.Combine(KineticDir, $"{safe} {DateTime.Now:yyyy-MM-dd HHmmss}.mp4");
                try
                {
                    var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    _kineticWrites[id] = new KineticWrite(fs, path, (int?)msg["layer"] ?? 0, (int?)msg["column"] ?? 0, name,
                        msg["project"]?.ToJsonString() ?? "{}", _comp.Current.Id);
                }
                catch (Exception ex) { Send(new { type = "error", message = $"Não consegui criar o vídeo da animação: {ex.Message}" }); }
                break;
            }
            case "kineticChunk":
            {
                if ((string?)msg["id"] is not { } id || !_kineticWrites.TryGetValue(id, out var w)) break;
                var bytes = Convert.FromBase64String((string?)msg["data"] ?? "");
                w.File.Seek((long?)msg["pos"] ?? w.File.Length, SeekOrigin.Begin);
                w.File.Write(bytes);
                break;
            }
            case "kineticEnd":
            {
                if ((string?)msg["id"] is not { } id || !_kineticWrites.Remove(id, out var w)) break;
                w.File.Dispose();
                if ((bool?)msg["ok"] != true)
                {
                    try { File.Delete(w.Path); } catch { }
                    break;
                }
                File.WriteAllText(w.Path + KineticProjectExt, w.Project);
                // volta para a aba de onde o editor foi aberto, se ainda existir
                if (w.DeckId != null && w.DeckId != _comp.Current.Id) { Send(new { type = "kineticSaved", path = w.Path, placed = false }); break; }
                if (w.Layer >= _comp.Layers.Count || w.Column >= _comp.Columns) break;
                var old = _comp.Layers[w.Layer].Clips[w.Column];
                var clip = Clip.FromFile(w.Path);
                clip.Name = w.Name;
                // trocando uma animação já existente: mantém os ajustes do clip (fim, efeitos, tamanho...)
                if (old?.Path != null && IsKineticFile(old.Path)) clip.Props = old.Props;
                _comp.Layers[w.Layer].Clips[w.Column] = clip;
                if (_comp.ActiveHere(_comp.Layers[w.Layer]) == w.Column) Trigger(w.Layer, w.Column);
                CompositionChanged();
                Send(new { type = "kineticSaved", path = w.Path, placed = true });
                break;
            }
            case "kineticOpen":
            {
                // reabrir no editor a animação que está nesta célula
                int layer = (int?)msg["layer"] ?? -1, column = (int?)msg["column"] ?? -1;
                if (layer < 0 || layer >= _comp.Layers.Count || column < 0 || column >= _comp.Columns) break;
                var path = _comp.Layers[layer].Clips[column]?.Path;
                if (path == null || !File.Exists(path + KineticProjectExt))
                {
                    Send(new { type = "error", message = "Não achei o projeto desta animação (o arquivo .vjanim.json ao lado do vídeo)." });
                    break;
                }
                try
                {
                    var project = JsonNode.Parse(File.ReadAllText(path + KineticProjectExt));
                    Send(new { type = "kineticProject", layer, column, project });
                }
                catch (Exception ex) { Send(new { type = "error", message = $"Não consegui abrir o projeto da animação: {ex.Message}" }); }
                break;
            }
        }
    }

    static bool IsKineticFile(string path) => File.Exists(path + KineticProjectExt);
}
