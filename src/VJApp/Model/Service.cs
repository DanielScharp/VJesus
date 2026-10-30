using System.Text.Json;

namespace VJApp.Model;

/// <summary>Um passo do roteiro do culto.</summary>
sealed class ServiceItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    /// <summary>song, bible, text, clip, column, notice, timer ou clear.</summary>
    public string Kind { get; set; } = "text";
    public string Title { get; set; } = "";
    /// <summary>Anotação só para o operador (quem fala, o que combinar).</summary>
    public string? Note { get; set; }
    public string? SongId { get; set; }
    public string? Book { get; set; }
    public int Chapter { get; set; }
    public int Verse { get; set; }
    public int VerseEnd { get; set; }
    /// <summary>Texto rápido ou aviso.</summary>
    public string? Text { get; set; }
    public string? DeckId { get; set; }
    public int Layer { get; set; }
    public int Column { get; set; }
    /// <summary>Duração da contagem regressiva.</summary>
    public double Seconds { get; set; }
}

/// <summary>Um aviso da lista (aparece no telão por cima de tudo).</summary>
sealed class NoticeItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Text { get; set; } = "";
}

/// <summary>Como a contagem regressiva e os avisos aparecem no telão.</summary>
sealed class OverlaySettings
{
    public List<NoticeItem> Notices { get; set; } = [];
    /// <summary>Quanto tempo cada aviso fica quando eles passam em sequência.</summary>
    public float NoticeSeconds { get; set; } = 8;
    public bool NoticeBackground { get; set; }
    public LyricStyle NoticeStyle { get; set; } = new()
    {
        FontSize = 64, Position = "bottom", Box = true, OutlineWidth = 0, Shadow = false, Margin = 80,
    };
    public string TimerLabel { get; set; } = "O culto começa em";
    public string TimerEndText { get; set; } = "";
    public bool TimerBackground { get; set; } = true;
    public LyricStyle TimerStyle { get; set; } = new() { FontSize = 200, OutlineWidth = 0, Transition = "corte", FadeMs = 0 };
}

/// <summary>Roteiro do culto (a lista em ordem) mais a contagem e os avisos. Salvo em culto.json e no projeto.</summary>
sealed class ServiceLibrary
{
    public string Name { get; set; } = "Culto";
    public List<ServiceItem> Items { get; set; } = [];
    public OverlaySettings Overlay { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string DefaultPath => Path.Combine(AppInfo.DataDir, "culto.json");

    public static ServiceLibrary LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<ServiceLibrary>(File.ReadAllText(path), JsonOptions) is { } lib) return lib;
        }
        catch { /* arquivo corrompido: começa do zero */ }
        return new ServiceLibrary();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}