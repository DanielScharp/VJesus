using System.Text.Json;
using System.Text.Json.Serialization;

using VJApp;

namespace VJApp.Model;

/// <summary>Tipo de fonte de um clip.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ClipKind>))]
enum ClipKind { Video, Plasma, TestCard, Camera, Spout, Lyrics, Ndi }

/// <summary>Transição ao trocar o clip de uma layer (a ordem bate com o shader).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransitionType>))]
enum TransitionType { Cut, Crossfade, Dip, Wipe }

/// <summary>Blend modes disponíveis por layer (a ordem bate com o shader Composite.hlsl).</summary>
enum BlendMode { Normal, Add, Multiply, Screen, Overlay, Difference, Lighten, Darken }

sealed class Clip
{
    public ClipKind Kind { get; set; }
    public string? Path { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Fundo gerado (Plasma): tipo, cores e movimento. null = arco-íris clássico.</summary>
    public GeneratorSettings? Gen { get; set; }
    /// <summary>Câmera/placa de captura: resolução pedida (ex.: "1920x1080"; null = a padrão do aparelho).</summary>
    public string? CaptureSize { get; set; }
    /// <summary>Câmera/placa de captura: quadros por segundo pedidos (null = padrão).</summary>
    public int? CaptureFps { get; set; }

    public static Clip FromFile(string path) =>
        new() { Kind = ClipKind.Video, Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };

    /// <summary>Entrada ao vivo: câmera/placa de captura (DirectShow), sender Spout ou fonte NDI (Path = nome).</summary>
    public static Clip Live(ClipKind kind, string name) => new() { Kind = kind, Path = name, Name = name };

    public static Clip Generator(ClipKind kind) =>
        new() { Kind = kind, Name = kind == ClipKind.Plasma ? "Plasma" : "Test card" };
}

/// <summary>Ajustes do fundo gerado (para fundos leves de culto).</summary>
sealed class GeneratorSettings
{
    /// <summary>ondas, nuvens, aurora, luzes, gradiente, feixes, arcoiris</summary>
    public string Type { get; set; } = "ondas";
    public float Speed { get; set; } = 0.4f;
    public float Intensity { get; set; } = 0.8f;
    public float Scale { get; set; } = 1f;
    public float Detail { get; set; } = 0.5f;
    /// <summary>Direção do movimento em graus.</summary>
    public float Angle { get; set; } = 90;
    public List<string> Colors { get; set; } = ["#0A1030", "#1E4A9A", "#8CC8FF"];
}

/// <summary>Um efeito aplicado numa layer ou no master, com os valores dos parâmetros.</summary>
sealed class EffectInstance
{
    public string Effect { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, float> Values { get; set; } = [];
}

sealed class Layer
{
    public string Name { get; set; } = "";
    /// <summary>Fundo gerado (Plasma): tipo, cores e movimento. null = arco-íris clássico.</summary>
    public GeneratorSettings? Gen { get; set; }
    /// <summary>Câmera/placa de captura: resolução pedida (ex.: "1920x1080"; null = a padrão do aparelho).</summary>
    public string? CaptureSize { get; set; }
    /// <summary>Câmera/placa de captura: quadros por segundo pedidos (null = padrão).</summary>
    public int? CaptureFps { get; set; }
    public float Opacity { get; set; } = 1f;
    public BlendMode Blend { get; set; }
    public Clip?[] Clips { get; set; } = [];
    public List<EffectInstance> Effects { get; set; } = [];
    public TransitionType Transition { get; set; } = TransitionType.Cut;
    public float TransitionSeconds { get; set; } = 1f;
    public float Volume { get; set; } = 1f;
    /// <summary>Quando esta layer está tocando, ela aparece no item "Telão" da tela de retorno.</summary>
    public bool SendToStage { get; set; }

    /// <summary>Coluna do clip tocando agora (-1 = layer vazia). Não é salvo.</summary>
    [JsonIgnore] public int Active { get; set; } = -1;
    /// <summary>Aba (deck) de onde veio o clip tocando, e o próprio clip (continua tocando ao trocar de aba).</summary>
    [JsonIgnore] public string? ActiveDeck { get; set; }
    [JsonIgnore] public Clip? PlayingClip { get; set; }
}

/// <summary>Uma aba de clips (como os decks do Resolume): a grade inteira de colunas de cada layer.</summary>
sealed class Deck
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Aba";
    public int Columns { get; set; } = Composition.DefaultColumns;
    /// <summary>Clips[layer][coluna].</summary>
    public List<Clip?[]> Clips { get; set; } = [];
    /// <summary>Nome de cada coluna (null/vazio = só o número).</summary>
    public List<string?> ColumnNames { get; set; } = [];
}

/// <summary>
/// A composição: uma grade de layers × colunas, igual ao Resolume.
/// Layer 0 fica embaixo (é desenhada primeiro).
/// </summary>
sealed class Composition
{
    public const int DefaultLayers = 4;
    public const int MaxLayers = 32;
    public const int DefaultColumns = 8;

    public int Columns { get; set; } = DefaultColumns;
    public List<Layer> Layers { get; set; } = [];
    public List<EffectInstance> MasterEffects { get; set; } = [];
    public float Bpm { get; set; } = 120f;
    public float MasterVolume { get; set; } = 1f;
    /// <summary>Abas de clips. A aba aberta fica em Columns e Layers[].Clips (cópia de trabalho).</summary>
    public List<Deck> Decks { get; set; } = [];
    /// <summary>Nomes das colunas da aba aberta (cópia de trabalho, como os clips).</summary>
    [JsonIgnore] public List<string?> ColumnNames { get; set; } = [];
    public int CurrentDeck { get; set; }

    public Deck Current => Decks[CurrentDeck];

    /// <summary>Guarda a grade aberta na aba atual (antes de salvar ou trocar de aba).</summary>
    public void StoreDeck()
    {
        if (Decks.Count == 0) return;
        Current.Columns = Columns;
        Current.Clips = [.. Layers.Select(l => l.Clips)];
        Current.ColumnNames = [.. ColumnNames.Take(Columns)];
    }

    /// <summary>Abre a aba <paramref name="index"/> (os clips tocando continuam tocando).</summary>
    public void LoadDeck(int index)
    {
        CurrentDeck = Math.Clamp(index, 0, Decks.Count - 1);
        var d = Current;
        Columns = Math.Max(1, d.Columns);
        ColumnNames = [.. d.ColumnNames.Take(Columns), .. Enumerable.Repeat<string?>(null, Math.Max(0, Columns - d.ColumnNames.Count))];
        for (int i = 0; i < Layers.Count; i++)
        {
            var row = i < d.Clips.Count ? d.Clips[i] : [];
            Layers[i].Clips = row.Length == Columns ? row : [.. row.Take(Columns), .. new Clip?[Math.Max(0, Columns - row.Length)]];
        }
        StoreDeck();
    }

    /// <summary>Coluna tocando nesta layer, se o clip é da aba aberta (senão -1).</summary>
    public int ActiveHere(Layer l) => Decks.Count > 0 && l.ActiveDeck == Current.Id ? l.Active : -1;

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => System.IO.Path.Combine(
        AppInfo.DataDir, "composicao.json");

    public static Composition CreateDefault()
    {
        var c = new Composition();
        for (int i = 0; i < DefaultLayers; i++)
            c.Layers.Add(new Layer { Name = $"Layer {i + 1}", Clips = new Clip?[DefaultColumns] });
        return c.Normalize();
    }

    public static Composition LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var c = JsonSerializer.Deserialize<Composition>(File.ReadAllText(path), JsonOptions);
                if (c is { Layers.Count: > 0 }) return c.Normalize();
            }
        }
        catch { /* arquivo corrompido: começa do zero */ }
        return CreateDefault();
    }

    /// <summary>Garante o número fixo de layers do motor e o tamanho das linhas da grade.</summary>
    public Composition Normalize()
    {
        if (Columns < 1) Columns = DefaultColumns;
        // quantas layers a pessoa quiser (de 1 a 32); projeto vazio começa com 4
        if (Layers.Count == 0) while (Layers.Count < DefaultLayers) Layers.Add(new Layer { Name = $"Layer {Layers.Count + 1}" });
        if (Layers.Count > MaxLayers) Layers.RemoveRange(MaxLayers, Layers.Count - MaxLayers);
        foreach (var l in Layers)
        {
            if (l.Clips.Length != Columns) l.Clips = [.. l.Clips.Take(Columns), .. new Clip?[Math.Max(0, Columns - l.Clips.Length)]];
            l.Active = -1;
        }
        if (Decks.Count == 0) Decks.Add(new Deck { Name = "Principal", Columns = Columns, Clips = [.. Layers.Select(l => l.Clips)] });
        LoadDeck(CurrentDeck);
        return this;
    }

    public void Save(string path)
    {
        StoreDeck();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
