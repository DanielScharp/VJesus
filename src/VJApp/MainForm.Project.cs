using System.Text.Json;
using VJApp.Model;

namespace VJApp;

/// <summary>Arquivo de projeto (.vjproj): a grade de clips com efeitos + o mapeamento do palco.</summary>
sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public Composition Composition { get; set; } = Composition.CreateDefault();
    public MappingSetup Mapping { get; set; } = MappingSetup.CreateDefault();
}

/// <summary>Projetos (novo/abrir/salvar) e presets de mapeamento.</summary>
sealed partial class MainForm
{
    const string ProjectFilter = "Projeto VJApp|*.vjproj|Todos os arquivos|*.*";
    static readonly JsonSerializerOptions ProjectJson = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    static string AppDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp");
    static string PresetDir => Path.Combine(AppDataDir, "presets", "mapeamento");
    static string LastProjectFile => Path.Combine(AppDataDir, "ultimo-projeto.txt");

    string? _projectPath = File.Exists(LastProjectFile) ? File.ReadAllText(LastProjectFile).Trim() is { Length: > 0 } p && File.Exists(p) ? p : null : null;

    // ---------------- projetos ----------------

    void NewProject()
    {
        // projeto novo: grade vazia, mantém o mapeamento do palco atual
        LoadState(Composition.CreateDefault(), _mapping);
        SetProjectPath(null);
    }

    void OpenProject()
    {
        using var dlg = new OpenFileDialog { Filter = ProjectFilter, Title = "Abrir projeto" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var proj = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(dlg.FileName), ProjectJson)
                ?? throw new InvalidDataException("arquivo vazio");
            LoadState(proj.Composition.Normalize(), proj.Mapping.Screens.Count > 0 ? proj.Mapping : MappingSetup.CreateDefault());
            SetProjectPath(dlg.FileName);

            var missing = _comp.Layers.SelectMany(l => l.Clips)
                .Where(c => c?.Kind == ClipKind.Video && !File.Exists(c.Path)).Select(c => c!.Name).Distinct().ToList();
            if (missing.Count > 0)
                Send(new { type = "error", message = $"{missing.Count} vídeo(s) do projeto não foram encontrados: {string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? "…" : "")}" });
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
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new ProjectFile { Composition = _comp, Mapping = _mapping }, ProjectJson));
            SetProjectPath(path);
            Send(new { type = "notice", message = $"Projeto salvo: {Path.GetFileName(path)}" });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui salvar: {ex.Message}" });
        }
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
