using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Arquivo de projeto (.vjproj): grade de clips com efeitos, mapeamento do palco, letras (músicas,
/// cenas, estilo, transições e animações), tela de retorno e ataques. Os vídeos e imagens usados
/// são copiados para a pasta "&lt;projeto&gt; - mídia" ao lado do arquivo, então o projeto inteiro
/// pode ser levado para outro computador.
/// </summary>
sealed class ProjectFile
{
    public int Version { get; set; } = 2;
    public Composition Composition { get; set; } = Composition.CreateDefault();
    public MappingSetup Mapping { get; set; } = MappingSetup.CreateDefault();
    public LyricsLibrary? Lyrics { get; set; }
    public StageConfig? Stage { get; set; }
    public AttackLibrary? Attacks { get; set; }
}

/// <summary>Projetos (novo/abrir/salvar/salvamento automático) e presets de mapeamento.</summary>
sealed partial class MainForm
{
    const string ProjectFilter = "Projeto VJApp|*.vjproj|Todos os arquivos|*.*";
    static readonly JsonSerializerOptions ProjectJson = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp");
    static string PresetDir => Path.Combine(AppDataDir, "presets", "mapeamento");
    static string LastProjectFile => Path.Combine(AppDataDir, "ultimo-projeto.txt");

    string? _projectPath = File.Exists(LastProjectFile) ? File.ReadAllText(LastProjectFile).Trim() is { Length: > 0 } p && File.Exists(p) ? p : null : null;

    static string AutosaveDir => Path.Combine(AppDataDir, "autosave");

    /// <summary>Projeto passado na linha de comando, aberto quando a interface fica pronta.</summary>
    public string? StartupProject;

    readonly System.Windows.Forms.Timer _autosaveTimer = new() { Interval = 60_000 };
    string? _lastSavedHash;
    bool _saving;

    static string MediaDirFor(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(projectPath))!, Path.GetFileNameWithoutExtension(projectPath) + " - mídia");

    // ---------------- projetos ----------------

    void NewProject()
    {
        // projeto novo: grade vazia, mantém o mapeamento do palco, as letras e o retorno atuais
        LoadState(Composition.CreateDefault(), _mapping);
        SetProjectPath(null);
        _lastSavedHash = null;
    }

    void OpenProject()
    {
        using var dlg = new OpenFileDialog { Filter = ProjectFilter, Title = "Abrir projeto" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        OpenProjectFile(dlg.FileName);
    }

    void OpenProjectFile(string file)
    {
        try
        {
            var proj = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(file), ProjectJson)
                ?? throw new InvalidDataException("arquivo vazio");
            ResolveMediaPaths(proj.Composition, file);

            // projetos antigos (versão 1) não têm letras/retorno/ataques: mantém os atuais
            if (proj.Lyrics != null)
            {
                _lyrics = proj.Lyrics;
                _lyrics.Save(LyricsLibrary.DefaultPath);
            }
            if (proj.Stage != null)
            {
                foreach (var (k, v) in StageConfig.DefaultElements()) proj.Stage.Elements.TryAdd(k, v);
                _stage = proj.Stage;
                _stage.Save(StageConfig.DefaultPath);
            }
            if (proj.Attacks != null)
            {
                _attackLib = proj.Attacks;
                _attackLib.Save(AttackLibrary.DefaultPath);
            }
            LoadState(proj.Composition.Normalize(), proj.Mapping.Screens.Count > 0 ? proj.Mapping : MappingSetup.CreateDefault());
            SetProjectPath(file);
            _lastSavedHash = Hash(BuildProjectJson(file));
            SendLyricsLibrary();
            SendLyricsState();
            ApplyStageWindow();
            SendStage();
            SendAttacks();
            BroadcastRemoteState();

            var missing = AllClips(_comp)
                .Where(c => c?.Kind == ClipKind.Video && !File.Exists(c.Path)).Select(c => c!.Name).Distinct().ToList();
            if (missing.Count > 0)
                Send(new { type = "error", message = $"{missing.Count} vídeo(s)/imagem(ns) do projeto não foram encontrados: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? "…" : "")}" });
            else
                Send(new { type = "notice", message = $"Projeto aberto: {Path.GetFileNameWithoutExtension(file)}" });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui abrir o projeto: {ex.Message}" });
        }
    }

    void SaveProject(bool saveAs)
    {
        string? path = _projectPath;
        if (saveAs || path == null)
        {
            using var dlg = new SaveFileDialog
            {
                Filter = ProjectFilter, Title = "Salvar projeto", DefaultExt = "vjproj",
                FileName = path != null ? Path.GetFileName(path) : "Meu show.vjproj",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            path = dlg.FileName;
        }
        _ = WriteProjectAsync(path, silent: false);
    }

    /// <summary>
    /// Copia para a pasta do projeto a mídia que ainda não está lá (em segundo plano: vídeos podem ser
    /// grandes) e grava o .vjproj com os caminhos relativos a essa pasta.
    /// </summary>
    async Task WriteProjectAsync(string path, bool silent)
    {
        if (_saving) return;
        _saving = true;
        try
        {
            var media = MediaDirFor(path);
            var files = AllClips(_comp)
                .Where(c => c?.Kind == ClipKind.Video && !string.IsNullOrEmpty(c.Path) && File.Exists(c.Path))
                .Select(c => Path.GetFullPath(c!.Path!)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var toCopy = files.Where(f => !IsInside(f, media) && !SameFileExists(f, Path.Combine(media, Path.GetFileName(f)))).ToList();
            if (toCopy.Count > 0)
                Send(new { type = "notice", message = $"Copiando {toCopy.Count} vídeo(s)/imagem(ns) para a pasta do projeto…" });
            await Task.Run(() =>
            {
                if (files.Count > 0) Directory.CreateDirectory(media);
                foreach (var f in toCopy) File.Copy(f, Path.Combine(media, Path.GetFileName(f)), overwrite: true);
            });

            var json = BuildProjectJson(path);
            var tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json);
            File.Move(tmp, path, overwrite: true);
            _lastSavedHash = Hash(json);
            if (_projectPath != path) SetProjectPath(path);
            Send(new { type = "notice", message = silent ? $"Salvo automaticamente às {DateTime.Now:HH:mm}" : $"Projeto salvo: {Path.GetFileName(path)}" });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui salvar: {ex.Message}" });
        }
        finally { _saving = false; }
    }

    /// <summary>Projeto em JSON, com os caminhos de mídia apontando para a pasta "- mídia" do projeto.</summary>
    string BuildProjectJson(string path)
    {
        var mediaName = Path.GetFileName(MediaDirFor(path));
        var comp = JsonSerializer.Deserialize<Composition>(JsonSerializer.Serialize(_comp, ProjectJson), ProjectJson)!;
        foreach (var c in AllClips(comp))
            if (c?.Kind == ClipKind.Video && !string.IsNullOrEmpty(c.Path))
                c.Path = Path.Combine(mediaName, Path.GetFileName(c.Path)); // relativo ao .vjproj
        return SerializeProject(comp);
    }

    string SerializeProject(Composition comp) => JsonSerializer.Serialize(new ProjectFile
    {
        Composition = comp, Mapping = _mapping, Lyrics = _lyrics, Stage = _stage, Attacks = _attackLib,
    }, ProjectJson);

    /// <summary>Caminhos relativos viram absolutos; um vídeo que sumiu do lugar é procurado na pasta de mídia do projeto.</summary>
    static void ResolveMediaPaths(Composition comp, string projectFile)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        var media = MediaDirFor(projectFile);
        foreach (var c in AllClips(comp))
        {
            if (c?.Kind != ClipKind.Video || string.IsNullOrEmpty(c.Path)) continue;
            if (!Path.IsPathRooted(c.Path)) c.Path = Path.GetFullPath(Path.Combine(dir, c.Path));
            var local = Path.Combine(media, Path.GetFileName(c.Path));
            if (!File.Exists(c.Path) && File.Exists(local)) c.Path = local;
        }
    }

    /// <summary>Clips de todas as abas (a aba aberta também está nas layers).</summary>
    static IEnumerable<Clip?> AllClips(Composition c)
    {
        c.StoreDeck();
        return c.Layers.SelectMany(l => l.Clips).Concat(c.Decks.SelectMany(d => d.Clips).SelectMany(r => r)).Distinct();
    }

    static bool IsInside(string file, string dir) =>
        Path.GetFullPath(file).StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    static bool SameFileExists(string src, string dst) =>
        File.Exists(dst) && new FileInfo(dst).Length == new FileInfo(src).Length;

    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));

    /// <summary>
    /// A cada minuto, se algo mudou, grava no projeto aberto. Sem projeto aberto, guarda uma cópia de
    /// segurança em %AppData%\VJApp\autosave (com os caminhos originais dos vídeos).
    /// </summary>
    void StartAutosave()
    {
        if (_projectPath != null) try { _lastSavedHash = Hash(BuildProjectJson(_projectPath)); } catch { }
        _autosaveTimer.Tick += (_, _) =>
        {
            if (_saving) return;
            try
            {
                if (_projectPath != null)
                {
                    if (Hash(BuildProjectJson(_projectPath)) != _lastSavedHash) _ = WriteProjectAsync(_projectPath, silent: true);
                    return;
                }
                var json = SerializeProject(_comp);
                var h = Hash(json);
                if (h == _lastSavedHash) return;
                Directory.CreateDirectory(AutosaveDir);
                File.WriteAllText(Path.Combine(AutosaveDir, "Sem título (automático).vjproj"), json);
                _lastSavedHash = h;
            }
            catch { /* tenta de novo no próximo minuto */ }
        };
        _autosaveTimer.Start();
    }

    /// <summary>Troca a composição e o mapeamento em uso (para tudo, recarrega o motor e a UI).</summary>
    void LoadState(Composition comp, MappingSetup mapping)
    {
        for (int i = 0; i < _comp.Layers.Count; i++) ClearLayer(i);
        _comp = comp;
        _mapping = mapping;
        for (int i = 0; i < _comp.Layers.Count; i++) ApplyLayerParams(i);
        _renderer?.SetBpm(_comp.Bpm);
        if (_audio != null) _audio.MasterVolume = _comp.MasterVolume;
        RebuildEffectChains(resync: true);
        ApplyMapping();
        _comp.Save(Composition.DefaultPath);
        _mapping.Save(MappingSetup.DefaultPath);
        SendComposition();
        SendMapping();
        SendTempo();
    }

    void SetProjectPath(string? path)
    {
        _projectPath = path;
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(LastProjectFile, path ?? "");
        SendProjectInfo();
    }

    void SendProjectInfo() => Send(new
    {
        type = "project",
        name = _projectPath != null ? Path.GetFileNameWithoutExtension(_projectPath) : null,
        path = _projectPath,
    });

    // ---------------- presets de mapeamento ----------------

    static string PresetPath(string name) =>
        Path.Combine(PresetDir, string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim() + ".json");

    void SaveMappingPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Directory.CreateDirectory(PresetDir);
        _mapping.Save(PresetPath(name));
        SendPresets();
        Send(new { type = "notice", message = $"Preset \"{name.Trim()}\" salvo" });
    }

    void LoadMappingPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !File.Exists(PresetPath(name))) return;
        _mapping = MappingSetup.LoadOrDefault(PresetPath(name));
        ApplyMapping();
        _mapping.Save(MappingSetup.DefaultPath);
        SendMapping();
    }

    void DeleteMappingPreset(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (File.Exists(PresetPath(name))) File.Delete(PresetPath(name));
        SendPresets();
    }

    void SendPresets() => Send(new
    {
        type = "presets",
        list = Directory.Exists(PresetDir)
            ? Directory.GetFiles(PresetDir, "*.json").Select(Path.GetFileNameWithoutExtension).Order().ToArray()
            : [],
    });
}

/// <summary>Atalho do VJApp na área de trabalho (criado uma vez; se a pessoa apagar, não volta).</summary>
sealed partial class MainForm
{
    static void EnsureDesktopShortcut()
    {
        try
        {
            var flag = Path.Combine(AppDataDir, "atalho-criado.txt");
            var lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VJApp.lnk");
            var exe = Environment.ProcessPath;
            if (exe == null || File.Exists(lnk) || File.Exists(flag)) return;
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic sc = shell.CreateShortcut(lnk);
            sc.TargetPath = exe;
            sc.WorkingDirectory = Path.GetDirectoryName(exe);
            sc.IconLocation = exe + ",0";
            sc.Description = "VJApp: VJ, projection mapping e letras";
            sc.Save();
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(flag, lnk);
        }
        catch { /* sem atalho não impede o programa */ }
    }
}

sealed partial class MainForm
{
    /// <summary>Duplo clique num .vjproj abre no VJApp (registro só do usuário, aponta para este .exe).</summary>
    static void RegisterProjectFileType()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            using var ext = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\.vjproj");
            ext.SetValue("", "VJApp.Projeto");
            using var type = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\VJApp.Projeto");
            type.SetValue("", "Projeto VJApp");
            using var icon = type.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var cmd = type.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{exe}\" \"%1\"");
        }
        catch { /* sem associação, o Abrir… continua funcionando */ }
    }
}
