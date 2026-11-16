using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Biblioteca de mídia: todos os vídeos, imagens e apresentações da igreja num lugar só (as pastas
/// escolhidas e o que já está na grade), com busca, tags ("louvor", "ceia"...), prévia animada ao passar
/// o mouse e arrastar para a grade.
/// </summary>
sealed partial class MainForm
{
    sealed class LibrarySettings
    {
        public List<string> Folders { get; set; } = [];
        /// <summary>Tags por arquivo (caminho completo).</summary>
        public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Pastas da biblioteca (só para organizar; os arquivos não saem do lugar no disco).</summary>
        public List<LibraryFolder> Collections { get; set; } = [];
        /// <summary>Em que pasta da biblioteca cada arquivo está (caminho completo → id da pasta).</summary>
        public Dictionary<string, string> Placement { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Arquivos soltos trazidos para a biblioteca (fora das pastas do computador lidas por ela).</summary>
        public List<string> Files { get; set; } = [];
    }

    sealed class LibraryFolder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string Name { get; set; } = "Pasta";
        public string? Parent { get; set; }
        /// <summary>Pasta do computador de onde ela veio (arrastada ou escolhida em "+ Pasta do computador").</summary>
        public string? Source { get; set; }
    }

    static string LibraryPath => Path.Combine(AppDataDir, "biblioteca.json");
    LibrarySettings _library = LoadLibrary();
    readonly Dictionary<string, List<string>> _previewStrips = [];
    int _libraryScan;

    static readonly string[] VideoExtensions = [".mp4", ".mov", ".m4v", ".mkv", ".avi", ".wmv", ".webm", ".mpg", ".mpeg", ".mxf", ".ts"];
    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif", ".tif", ".tiff"];

    static LibrarySettings LoadLibrary()
    {
        try
        {
            if (File.Exists(LibraryPath) && JsonSerializer.Deserialize<LibrarySettings>(File.ReadAllText(LibraryPath)) is { } s)
            {
                s.Tags = new(s.Tags ?? [], StringComparer.OrdinalIgnoreCase);
                s.Placement = new(s.Placement ?? [], StringComparer.OrdinalIgnoreCase);
                s.Collections ??= [];
                s.Files ??= [];
                // pastas importadas antes de guardar a origem: liga pelo nome
                foreach (var dir in s.Folders)
                    if (s.Collections.All(c => !string.Equals(c.Source, dir, StringComparison.OrdinalIgnoreCase))
                        && s.Collections.Where(c => c.Parent == null && c.Source == null && c.Name == Path.GetFileName(dir.TrimEnd('\\')))
                            .OrderByDescending(c => s.Placement.Count(p => p.Value == c.Id)).FirstOrDefault() is { } match)
                        match.Source = dir;
                return s;
            }
        }
        catch { }
        return new();
    }

    void SaveLibrary()
    {
        try { File.WriteAllText(LibraryPath, JsonSerializer.Serialize(_library, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Send(new { type = "error", message = "Não consegui salvar a biblioteca: " + ex.Message }); }
    }

    void OnLibraryMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "libraryGet":
                _ = SendLibraryAsync();
                break;
            case "libraryAddFolder":
            {
                using var dlg = new FolderBrowserDialog { Description = "Pasta com vídeos, imagens ou apresentações da igreja (as subpastas entram junto)" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                // igual a arrastar a pasta: vira uma pasta da biblioteca com o mesmo nome
                LibraryDrop([dlg.SelectedPath], (string?)msg["folder"]);
                break;
            }
            case "libraryRemoveFolder":
                _library.Folders.RemoveAll(f => string.Equals(f, (string?)msg["path"], StringComparison.OrdinalIgnoreCase));
                SaveLibrary();
                _ = SendLibraryAsync();
                break;
            case "libraryTags":
            {
                var path = (string?)msg["path"];
                if (path == null) return;
                var tags = (msg["tags"] as JsonArray)?.Select(t => ((string?)t)?.Trim().ToLowerInvariant()).Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t!).Distinct().ToList() ?? [];
                if (tags.Count == 0) _library.Tags.Remove(path); else _library.Tags[path] = tags;
                SaveLibrary();
                Send(new { type = "libraryTags", path, tags });
                break;
            }
            case "libraryFolderNew":
            {
                string? parent = (string?)msg["parent"];
                if (parent != null && _library.Collections.All(c => c.Id != parent)) parent = null;
                var folder = new LibraryFolder { Name = CleanFolderName((string?)msg["name"]), Parent = parent };
                _library.Collections.Add(folder);
                SaveLibrary();
                SendLibraryCollections(created: folder.Id);
                break;
            }
            case "libraryFolderRename":
                if (_library.Collections.FirstOrDefault(c => c.Id == (string?)msg["id"]) is { } rf) { rf.Name = CleanFolderName((string?)msg["name"]); SaveLibrary(); SendLibraryCollections(); }
                break;
            case "libraryFolderMove":
            {
                // pasta dentro de outra (não deixa pôr dentro dela mesma ou de uma filha)
                if (_library.Collections.FirstOrDefault(c => c.Id == (string?)msg["id"]) is not { } mf) break;
                string? target = (string?)msg["parent"];
                for (var p = target; p != null; p = _library.Collections.FirstOrDefault(c => c.Id == p)?.Parent)
                    if (p == mf.Id) return;
                mf.Parent = target;
                SaveLibrary();
                SendLibraryCollections();
                break;
            }
            case "libraryFolderDelete":
            {
                // apaga só a pasta da biblioteca: o que estava nela (arquivos e subpastas) sobe um nível.
                // Se ela veio de uma pasta do computador, a biblioteca para de ler aquela pasta (e some com o que era dela).
                if (_library.Collections.FirstOrDefault(c => c.Id == (string?)msg["id"]) is not { } df) break;
                if (df.Source != null)
                {
                    var gone = new HashSet<string>([df.Id]);
                    for (bool more = true; more;) { more = false; foreach (var c in _library.Collections) if (c.Parent != null && gone.Contains(c.Parent) && gone.Add(c.Id)) more = true; }
                    foreach (var k in _library.Placement.Where(p => gone.Contains(p.Value)).Select(p => p.Key).ToList()) _library.Placement.Remove(k);
                    _library.Collections.RemoveAll(c => gone.Contains(c.Id));
                    _library.Folders.RemoveAll(d => string.Equals(d, df.Source, StringComparison.OrdinalIgnoreCase));
                    SaveLibrary();
                    SendLibraryCollections();
                    _ = SendLibraryAsync();
                    break;
                }
                foreach (var child in _library.Collections.Where(c => c.Parent == df.Id)) child.Parent = df.Parent;
                foreach (var k in _library.Placement.Where(p => p.Value == df.Id).Select(p => p.Key).ToList())
                    if (df.Parent == null) _library.Placement.Remove(k); else _library.Placement[k] = df.Parent;
                _library.Collections.Remove(df);
                SaveLibrary();
                SendLibraryCollections();
                break;
            }
            case "libraryMove":
            {
                string? target = (string?)msg["folder"];
                if (target != null && _library.Collections.All(c => c.Id != target)) break;
                foreach (var p in (msg["paths"] as JsonArray ?? []).Select(x => (string?)x).Where(x => x != null))
                    if (target == null) _library.Placement.Remove(p!); else _library.Placement[p!] = target;
                SaveLibrary();
                SendLibraryCollections();
                break;
            }
            case "libraryThumbs":
                foreach (var p in (msg["paths"] as JsonArray ?? []).Select(x => (string?)x).Where(x => x != null).Take(60))
                    RequestThumb(p!);
                break;
            case "libraryPreview":
            {
                var path = (string?)msg["path"];
                if (path == null || !File.Exists(path)) return;
                if (_previewStrips.TryGetValue(path, out var cached)) { Send(new { type = "libraryPreview", path, frames = cached }); return; }
                Task.Run(() =>
                {
                    var frames = Thumbnailer.CreateStrip(path);
                    BeginInvoke(() =>
                    {
                        _previewStrips[path] = frames;
                        Send(new { type = "libraryPreview", path, frames });
                    });
                });
                break;
            }
            case "libraryPlace":
            {
                int layer = (int?)msg["layer"] ?? -1, column = (int?)msg["column"] ?? -1;
                var paths = (msg["paths"] as JsonArray ?? []).Select(x => (string?)x).Where(x => x != null && (File.Exists(x) || Directory.Exists(x))).Select(x => x!).ToList();
                if (layer < 0 || layer >= _comp.Layers.Count || column < 0 || column >= _comp.Columns || paths.Count == 0) return;
                // apresentação já preparada (pasta de slides): entra como apresentação; arquivo: como se viesse do Explorer
                EnsureColumns(column + paths.Count);
                int c = column;
                foreach (var p in paths)
                {
                    if (Directory.Exists(p))
                    {
                        if (c >= _comp.Columns) break;
                        _comp.Layers[layer].Clips[c++] = new Clip { Kind = ClipKind.Presentation, Path = p, Name = PresentationName(p) };
                    }
                    else { PlaceFiles(layer, c, [p]); c++; }
                }
                CompositionChanged();
                break;
            }
            case "libraryOpenDir":
            {
                // abre a pasta do computador de uma pasta da biblioteca
                var dir = (string?)msg["path"];
                if (dir != null && Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
                else Send(new { type = "error", message = "Essa pasta não existe mais no computador." });
                break;
            }
            case "libraryReveal":
            {
                var path = (string?)msg["path"];
                if (path != null && (File.Exists(path) || Directory.Exists(path)))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                break;
            }
        }
    }

    /// <summary>
    /// Pastas e arquivos arrastados do Explorer para a biblioteca. Pasta: passa a ser lida pela biblioteca e vira
    /// uma pasta da biblioteca com o mesmo nome (e as subpastas dentro), com as imagens e vídeos já organizados.
    /// Arquivo: entra na biblioteca (na pasta da biblioteca que estiver aberta).
    /// </summary>
    void LibraryDrop(string[] paths, string? target)
    {
        if (target != null && _library.Collections.All(c => c.Id != target)) target = null;
        bool IsMedia(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            return VideoExtensions.Contains(ext) || ImageExtensions.Contains(ext) || PresentationExtensions.Contains(ext);
        }
        bool Covered(string f) => _library.Folders.Any(d => f.StartsWith(d.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(f, d, StringComparison.OrdinalIgnoreCase));
        int files = 0, folders = 0;

        // a estrutura da pasta vira pastas da biblioteca (só as que têm imagem ou vídeo)
        string? Mirror(string dir, string? parent, int depth)
        {
            if (depth > 6 || files > 5000) return null;
            List<string> media, subs;
            try
            {
                media = [.. Directory.EnumerateFiles(dir).Where(IsMedia)];
                subs = [.. Directory.EnumerateDirectories(dir)];
            }
            catch { return null; }
            string name = CleanFolderName(Path.GetFileName(dir.TrimEnd('\\')));
            // arrastou a mesma pasta de novo: usa a pasta da biblioteca que já existe
            var folder = _library.Collections.FirstOrDefault(c => string.Equals(c.Source, dir, StringComparison.OrdinalIgnoreCase))
                ?? _library.Collections.FirstOrDefault(c => c.Parent == parent && c.Name == name && c.Source == null);
            bool created = folder == null;
            folder ??= new LibraryFolder { Name = name, Parent = parent };
            folder.Source = dir;
            if (created) _library.Collections.Add(folder);
            bool any = media.Count > 0;
            foreach (var f in media) { _library.Placement[f] = folder.Id; files++; }
            foreach (var s in subs) if (Mirror(s, folder.Id, depth + 1) != null) any = true;
            if (!any) { if (created) _library.Collections.Remove(folder); return null; }
            folders++;
            return folder.Id;
        }

        string? first = null;
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                if (!Covered(p))
                {
                    // pasta mais ampla que alguma já lida: fica só ela
                    _library.Folders.RemoveAll(d => d.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
                    _library.Folders.Add(p);
                }
                first ??= Mirror(p, target, 0);
            }
            else if (File.Exists(p) && IsMedia(p))
            {
                if (!Covered(p) && !_library.Files.Contains(p, StringComparer.OrdinalIgnoreCase)) _library.Files.Add(p);
                if (target != null) _library.Placement[p] = target; else _library.Placement.Remove(p);
                files++;
            }
        }
        SaveLibrary();
        SendLibraryCollections(created: null);
        _ = SendLibraryAsync();
        Send(new
        {
            type = "notice",
            message = files == 0 ? "Nenhuma imagem, vídeo ou apresentação encontrada no que foi arrastado."
                : $"Biblioteca: {files} arquivo(s) trazido(s){(folders > 0 ? $" em {folders} pasta(s)" : "")}.",
        });
        if (first != null) Send(new { type = "libraryOpenFolder", id = first });
    }

    static string CleanFolderName(string? name) => string.IsNullOrWhiteSpace(name) ? "Nova pasta" : name.Trim()[..Math.Min(name.Trim().Length, 60)];

    void SendLibraryCollections(string? created = null) =>
        Send(new { type = "libraryCollections", collections = _library.Collections, placement = _library.Placement, created });

    /// <summary>Nome da apresentação guardada (o arquivo de origem, se anotado; senão, o nome da pasta).</summary>
    static string PresentationName(string folder)
    {
        try
        {
            var origin = Path.Combine(folder, "origem.txt");
            if (File.Exists(origin)) return Path.GetFileNameWithoutExtension(File.ReadAllLines(origin).FirstOrDefault() ?? folder);
        }
        catch { }
        return Path.GetFileName(folder);
    }

    void RequestThumb(string path)
    {
        if (_thumbs.TryGetValue(path, out var cached))
        {
            if (cached != null) Send(new { type = "thumb", path, data = cached });
            return;
        }
        _thumbs[path] = null;
        string source = Directory.Exists(path) ? PresentationSlides(path).FirstOrDefault() ?? path : path;
        Task.Run(() =>
        {
            var data = Thumbnailer.Create(source);
            _thumbs[path] = data;
            if (data != null) Send(new { type = "thumb", path, data });
        });
    }

    /// <summary>Lista tudo (pastas da biblioteca + o que está na grade) e manda para a tela.</summary>
    async Task SendLibraryAsync()
    {
        int seq = ++_libraryScan;
        _comp.StoreDeck();
        var used = AllClips(_comp).Where(c => c?.Path != null).Select(c => c!).ToList();
        var usedPaths = new HashSet<string>(used.Select(c => c.Path!), StringComparer.OrdinalIgnoreCase);
        var folders = _library.Folders.ToList();
        var looseFiles = _library.Files.ToList();
        var presentations = used.Where(c => c.Kind == ClipKind.Presentation).ToList();
        Send(new { type = "library", scanning = true, folders, items = (object?)null });

        var items = await Task.Run(() =>
        {
            var list = new Dictionary<string, (string Path, string Name, string Kind, string Folder, long Size, DateTime Modified)>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders.Where(Directory.Exists))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 }))
                    {
                        if (list.Count >= 5000) break;
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        string? kind = VideoExtensions.Contains(ext) ? "video" : ImageExtensions.Contains(ext) ? "image"
                            : PresentationExtensions.Contains(ext) ? "presentationFile" : null;
                        if (kind == null) continue;
                        var info = new FileInfo(f);
                        list[f] = (f, Path.GetFileNameWithoutExtension(f), kind, Path.GetFileName(folder), info.Length, info.LastWriteTime);
                    }
                }
                catch { }
            }
            // o que está na grade e fora das pastas também aparece (para achar e marcar com tags)
            // arquivos soltos que foram arrastados para a biblioteca
            foreach (var f in looseFiles.Where(f => !list.ContainsKey(f) && File.Exists(f)))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                string? kind = VideoExtensions.Contains(ext) ? "video" : ImageExtensions.Contains(ext) ? "image" : PresentationExtensions.Contains(ext) ? "presentationFile" : null;
                if (kind == null) continue;
                var info = new FileInfo(f);
                list[f] = (f, Path.GetFileNameWithoutExtension(f), kind, "Trazidos", info.Length, info.LastWriteTime);
            }
            foreach (var p in usedPaths.Where(p => !list.ContainsKey(p)))
            {
                if (File.Exists(p))
                {
                    string ext = Path.GetExtension(p).ToLowerInvariant();
                    var info = new FileInfo(p);
                    list[p] = (p, Path.GetFileNameWithoutExtension(p), ImageExtensions.Contains(ext) ? "image" : "video", "Na grade", info.Length, info.LastWriteTime);
                }
                else if (Directory.Exists(p))
                {
                    list[p] = (p, PresentationName(p), "presentation", "Na grade", 0, Directory.GetLastWriteTime(p));
                }
            }
            return list.Values.ToList();
        });
        if (seq != _libraryScan) return; // pediram de novo enquanto lia
        Send(new
        {
            type = "library",
            scanning = false,
            folders,
            collections = _library.Collections,
            placement = _library.Placement,
            items = items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Select(i => new
            {
                path = i.Path, name = i.Name, kind = i.Kind, folder = i.Folder, size = i.Size,
                modified = i.Modified.ToString("yyyy-MM-dd"),
                tags = _library.Tags.TryGetValue(i.Path, out var t) ? t : [],
                used = usedPaths.Contains(i.Path),
            }),
        });
    }
}
