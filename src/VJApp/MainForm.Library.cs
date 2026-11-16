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
        /// <summary>Subpastas do computador que a pessoa tirou da biblioteca (não voltam ao reler a pasta).</summary>
        public List<string> Hidden { get; set; } = [];
    }

    sealed class LibraryFolder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public string Name { get; set; } = "Pasta";
        public string? Parent { get; set; }
        /// <summary>Pasta do computador de onde ela veio (arrastada ou escolhida em "+ Pasta do computador").</summary>
        public string? Source { get; set; }
        /// <summary>Ligada ao Google Drive: criar e renomear pastas aqui muda no Drive também (e o que muda lá aparece aqui).</summary>
        public bool Linked { get; set; }
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
                s.Hidden ??= [];
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
        UpdateLibraryWatchers();
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
            case "librarySync":
                SyncLibrarySources();
                break;
            case "libraryAddDrive":
                AddDriveFolder((string?)msg["folder"]);
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
                // dentro de uma pasta do Google Drive: cria a pasta de verdade no Drive
                if (_library.Collections.FirstOrDefault(c => c.Id == parent) is { Linked: true, Source: { } pdir })
                {
                    string dir = Path.Combine(pdir, folder.Name);
                    for (int n = 2; Directory.Exists(dir); n++) dir = Path.Combine(pdir, $"{folder.Name} ({n})");
                    try { Directory.CreateDirectory(dir); folder.Source = dir; folder.Linked = true; folder.Name = Path.GetFileName(dir); }
                    catch (Exception ex) { Send(new { type = "error", message = "Não consegui criar a pasta no Google Drive: " + ex.Message }); break; }
                }
                _library.Collections.Add(folder);
                SaveLibrary();
                SendLibraryCollections(created: folder.Id);
                break;
            }
            case "libraryFolderRename":
            {
                if (_library.Collections.FirstOrDefault(c => c.Id == (string?)msg["id"]) is not { } rf) break;
                string newName = CleanFolderName((string?)msg["name"]);
                // pasta do Google Drive: renomeia de verdade (e tudo que apontava para ela acompanha)
                if (rf.Linked && rf.Source is { } old && Directory.Exists(old))
                {
                    string newDir = Path.Combine(Path.GetDirectoryName(old.TrimEnd('\\'))!, string.Concat(newName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)));
                    if (!SamePath(newDir, old))
                    {
                        try { Directory.Move(old, newDir); LibraryPathRenamed(old, newDir); }
                        catch (Exception ex) { Send(new { type = "error", message = "Não consegui renomear a pasta no Google Drive: " + ex.Message }); SendLibraryCollections(); break; }
                    }
                }
                rf.Name = newName;
                SaveLibrary();
                SendLibraryCollections();
                break;
            }
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
                    // subpasta de uma pasta que continua sendo lida: não volta na próxima leitura
                    if (_library.Folders.Any(d => IsUnder(df.Source, d)) && !_library.Hidden.Contains(df.Source, StringComparer.OrdinalIgnoreCase)) _library.Hidden.Add(df.Source);
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
                var moving = (msg["paths"] as JsonArray ?? []).Select(x => (string?)x).Where(x => x != null).Select(x => x!).ToList();
                // para uma pasta do Google Drive: copia os arquivos para lá (o original fica onde está)
                if (_library.Collections.FirstOrDefault(c => c.Id == target) is { Linked: true, Source: { } } lf)
                {
                    CopyIntoLinked(moving, lf);
                    moving = [.. moving.Where(m => IsUnder(m, lf.Source!))];
                }
                foreach (var p in moving)
                    if (target == null) _library.Placement.Remove(p); else _library.Placement[p] = target;
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

    static bool IsLibraryMedia(string f)
    {
        string ext = Path.GetExtension(f).ToLowerInvariant();
        return VideoExtensions.Contains(ext) || ImageExtensions.Contains(ext) || PresentationExtensions.Contains(ext);
    }

    static bool SamePath(string? a, string? b) => a != null && b != null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    static bool IsUnder(string path, string dir) => path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    int _mirrorFiles, _mirrorFolders;

    /// <summary>
    /// A estrutura de uma pasta do computador vira pastas da biblioteca (com o mesmo nome e as subpastas dentro).
    /// Rodar de novo atualiza: o que já existe é reaproveitado (pela pasta de origem), o que é novo entra.
    /// Pastas ligadas (Google Drive) mantêm até as subpastas vazias, para espelhar o Drive como ele é.
    /// </summary>
    string? MirrorDir(string dir, string? parent, int depth, bool linked)
    {
        if (depth > 8 || _mirrorFiles > 8000) return null;
        List<string> media, subs;
        try
        {
            media = [.. Directory.EnumerateFiles(dir).Where(IsLibraryMedia)];
            subs = [.. Directory.EnumerateDirectories(dir).Where(d => !Path.GetFileName(d).StartsWith('.'))];
        }
        catch { return null; }
        if (_library.Hidden.Any(h => SamePath(h, dir))) return null;
        string name = CleanFolderName(Path.GetFileName(dir.TrimEnd('\\')));
        // a mesma pasta de novo: usa a pasta da biblioteca que já existe
        var folder = _library.Collections.FirstOrDefault(c => SamePath(c.Source, dir))
            ?? _library.Collections.FirstOrDefault(c => c.Parent == parent && c.Name == name && c.Source == null);
        bool created = folder == null;
        folder ??= new LibraryFolder { Name = name, Parent = parent };
        folder.Source = dir;
        if (linked) { folder.Linked = true; folder.Name = name; } // no Drive o nome é sempre o da pasta
        if (created) _library.Collections.Add(folder);
        bool any = media.Count > 0 || linked;
        foreach (var f in media) { _library.Placement[f] = folder.Id; _mirrorFiles++; }
        foreach (var s in subs) if (MirrorDir(s, folder.Id, depth + 1, linked) != null) any = true;
        if (!any) { if (created) _library.Collections.Remove(folder); return null; }
        if (created) _mirrorFolders++;
        return folder.Id;
    }

    /// <summary>
    /// Pastas e arquivos arrastados do Explorer para a biblioteca. Pasta: passa a ser lida pela biblioteca e vira
    /// uma pasta da biblioteca com o mesmo nome (e as subpastas dentro), com as imagens e vídeos já organizados.
    /// Arquivo: entra na biblioteca (na pasta da biblioteca que estiver aberta).
    /// </summary>
    void LibraryDrop(string[] paths, string? target, bool linked = false)
    {
        if (target != null && _library.Collections.All(c => c.Id != target)) target = null;
        bool Covered(string f) => _library.Folders.Any(d => IsUnder(f, d) || SamePath(f, d));
        _mirrorFiles = 0; _mirrorFolders = 0;
        int files = 0;
        string? first = null;
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                if (!Covered(p))
                {
                    // pasta mais ampla que alguma já lida: fica só ela
                    _library.Folders.RemoveAll(d => IsUnder(d, p));
                    _library.Folders.Add(p);
                }
                first ??= MirrorDir(p, target, 0, linked);
            }
            else if (File.Exists(p) && IsLibraryMedia(p))
            {
                if (!Covered(p) && !_library.Files.Contains(p, StringComparer.OrdinalIgnoreCase)) _library.Files.Add(p);
                if (target != null) _library.Placement[p] = target; else _library.Placement.Remove(p);
                files++;
            }
        }
        files += _mirrorFiles;
        SaveLibrary();
        SendLibraryCollections(created: null);
        _ = SendLibraryAsync();
        Send(new
        {
            type = "notice",
            message = files == 0 && !linked ? "Nenhuma imagem, vídeo ou apresentação encontrada no que foi arrastado."
                : linked ? $"Pasta do Google Drive ligada: {files} arquivo(s). O que mudar no Drive aparece aqui sozinho."
                : $"Biblioteca: {files} arquivo(s) trazido(s){(_mirrorFolders > 0 ? $" em {_mirrorFolders} pasta(s)" : "")}.",
        });
        if (first != null) Send(new { type = "libraryOpenFolder", id = first });
    }

    // ---------------- pastas ligadas: acompanhar as mudanças no computador / Google Drive

    readonly Dictionary<string, FileSystemWatcher> _libWatchers = new(StringComparer.OrdinalIgnoreCase);
    System.Windows.Forms.Timer? _libSyncTimer;

    /// <summary>Uma "escuta" por pasta do computador lida pela biblioteca: avisa quando algo é criado, apagado ou renomeado.</summary>
    void UpdateLibraryWatchers()
    {
        foreach (var root in _libWatchers.Keys.Where(k => !_library.Folders.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _libWatchers[root].Dispose();
            _libWatchers.Remove(root);
        }
        foreach (var root in _library.Folders.Where(f => !_libWatchers.ContainsKey(f) && Directory.Exists(f)))
        {
            try
            {
                var w = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                    InternalBufferSize = 64 * 1024,
                };
                w.Created += (_, _) => BeginInvoke(ScheduleLibrarySync);
                w.Deleted += (_, _) => BeginInvoke(ScheduleLibrarySync);
                w.Renamed += (_, ev) => BeginInvoke(() => { LibraryPathRenamed(ev.OldFullPath, ev.FullPath); ScheduleLibrarySync(); });
                w.Error += (_, _) => BeginInvoke(ScheduleLibrarySync);
                w.EnableRaisingEvents = true;
                _libWatchers[root] = w;
            }
            catch { /* pasta sem permissão ou unidade fora do ar: tenta de novo na próxima vez */ }
        }
    }

    /// <summary>Junta várias mudanças seguidas (o Drive sincronizando muitos arquivos) numa atualização só.</summary>
    void ScheduleLibrarySync()
    {
        if (_libSyncTimer == null)
        {
            _libSyncTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _libSyncTimer.Tick += (_, _) => { _libSyncTimer.Stop(); SyncLibrarySources(); };
        }
        _libSyncTimer.Stop();
        _libSyncTimer.Start();
    }

    /// <summary>Relê as pastas do computador: subpastas e arquivos novos entram, os que sumiram saem.</summary>
    void SyncLibrarySources()
    {
        _mirrorFiles = 0; _mirrorFolders = 0;
        foreach (var root in _library.Folders.ToList())
        {
            if (!Directory.Exists(root)) continue; // unidade fora do ar (Drive desconectado): não mexe em nada
            if (_library.Collections.FirstOrDefault(c => SamePath(c.Source, root)) is not { } top) continue;
            MirrorDir(root, top.Parent, 0, top.Linked);
            // pastas que não existem mais dentro desta pasta do computador: saem da biblioteca
            foreach (var gone in _library.Collections.Where(c => c.Source != null && IsUnder(c.Source, root) && !Directory.Exists(c.Source)).ToList())
            {
                foreach (var child in _library.Collections.Where(c => c.Parent == gone.Id)) child.Parent = gone.Parent;
                _library.Collections.Remove(gone);
            }
            foreach (var k in _library.Placement.Keys.Where(k => IsUnder(k, root) && !File.Exists(k)).ToList()) _library.Placement.Remove(k);
        }
        SaveLibrary();
        SendLibraryCollections();
        _ = SendLibraryAsync();
    }

    /// <summary>
    /// Arquivo ou pasta renomeado/movido no computador (ou no Drive): troca o caminho em tudo que apontava
    /// para ele, inclusive os clips do grid, para nada perder a referência.
    /// </summary>
    void LibraryPathRenamed(string oldPath, string newPath)
    {
        string Fix(string p) => SamePath(p, oldPath) ? newPath : IsUnder(p, oldPath) ? newPath + p[oldPath.TrimEnd('\\').Length..] : p;
        bool clipsChanged = false;
        _comp.StoreDeck();
        foreach (var clip in AllClips(_comp))
        {
            if (clip?.Path == null) continue;
            var n = Fix(clip.Path);
            if (n != clip.Path) { clip.Path = n; clipsChanged = true; }
        }
        foreach (var k in _library.Placement.Keys.ToList())
        {
            var n = Fix(k);
            if (n != k) { var v = _library.Placement[k]; _library.Placement.Remove(k); _library.Placement[n] = v; }
        }
        foreach (var k in _library.Tags.Keys.ToList())
        {
            var n = Fix(k);
            if (n != k) { var v = _library.Tags[k]; _library.Tags.Remove(k); _library.Tags[n] = v; }
        }
        for (int i = 0; i < _library.Files.Count; i++) _library.Files[i] = Fix(_library.Files[i]);
        for (int i = 0; i < _library.Folders.Count; i++) _library.Folders[i] = Fix(_library.Folders[i]);
        foreach (var c in _library.Collections)
        {
            if (c.Source == null) continue;
            var n = Fix(c.Source);
            if (n != c.Source) { c.Source = n; if (c.Linked) c.Name = CleanFolderName(Path.GetFileName(n.TrimEnd('\\'))); }
        }
        if (clipsChanged) CompositionChanged();
    }

    /// <summary>Google Drive para computador: a pasta "Meu Drive" (G:\Meu Drive ou outra letra). null = não instalado.</summary>
    static string? FindGoogleDrive()
    {
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                foreach (var name in new[] { "Meu Drive", "My Drive", "Mi unidad" })
                {
                    var p = Path.Combine(d.RootDirectory.FullName, name);
                    if (Directory.Exists(p)) return p;
                }
            }
            catch { }
        }
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive");
        return Directory.Exists(legacy) ? legacy : null;
    }

    /// <summary>Ligar uma pasta do Google Drive à biblioteca (escolhida dentro do Meu Drive).</summary>
    void AddDriveFolder(string? target)
    {
        var drive = FindGoogleDrive();
        if (drive == null)
        {
            Send(new
            {
                type = "driveMissing",
                message = "Não achei o Google Drive neste computador. Instale o \"Google Drive para computador\" (drive.google.com/download), entre com a conta da igreja e tente de novo: o Meu Drive aparece como uma unidade (ex.: G:) e o VJesus passa a acompanhar a pasta escolhida.",
            });
            return;
        }
        using var dlg = new FolderBrowserDialog { Description = "Escolha a pasta do Google Drive que fica ligada à biblioteca", InitialDirectory = drive, SelectedPath = drive, ShowNewFolderButton = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LibraryDrop([dlg.SelectedPath], target, linked: true);
    }

    /// <summary>Copia arquivos para dentro de uma pasta ligada (Google Drive) sem tirar o original do lugar.</summary>
    void CopyIntoLinked(List<string> paths, LibraryFolder folder)
    {
        var dir = folder.Source!;
        var todo = paths.Where(p => File.Exists(p) && !IsUnder(p, dir)).ToList();
        if (todo.Count == 0) return;
        Send(new { type = "notice", message = $"Copiando {todo.Count} arquivo(s) para \"{folder.Name}\" no Google Drive…" });
        Task.Run(() =>
        {
            int ok = 0;
            foreach (var p in todo)
            {
                try
                {
                    string dest = Path.Combine(dir, Path.GetFileName(p));
                    for (int n = 2; File.Exists(dest); n++) dest = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(p)} ({n}){Path.GetExtension(p)}");
                    File.Copy(p, dest);
                    ok++;
                }
                catch { }
            }
            BeginInvoke(() =>
            {
                SyncLibrarySources();
                Send(new { type = "notice", message = $"{ok} arquivo(s) copiado(s) para o Google Drive (\"{folder.Name}\")." });
            });
        });
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
