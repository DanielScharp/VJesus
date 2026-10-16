using System.Text.Json;
using System.Text.Json.Serialization;

using VJApp;

namespace VJApp.Model;

/// <summary>Tipo de ataque (a ordem bate com o shader PSAttack em Engine.hlsl).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AttackType>))]
enum AttackType { Flash, Strobe, Zoom, Shake, Invert, Ring, Sweep }

/// <summary>
/// Um ataque: efeito rápido por cima de tudo que entra, segura e some suavemente, voltando ao que
/// estava sendo projetado. Tempos em milissegundos.
/// </summary>
sealed class AttackPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Ataque";
    public AttackType Type { get; set; } = AttackType.Flash;
    public string Color { get; set; } = "#FFFFFF";
    public int AttackMs { get; set; }
    public int HoldMs { get; set; } = 60;
    public int ReleaseMs { get; set; } = 500;
    /// <summary>0..1: quanto o ataque cobre a imagem.</summary>
    public float Intensity { get; set; } = 1;
    /// <summary>Ajuste do tipo: zoom (quanto aproxima), tremor (força), onda/varredura (largura).</summary>
    public float Amount { get; set; } = 0.15f;
    /// <summary>Strobe: quantas piscadas durante o ataque.</summary>
    public int Count { get; set; } = 6;
    /// <summary>Tecla de atalho (KeyboardEvent.key), ex.: "1", "q", "F5".</summary>
    public string? Key { get; set; }
    /// <summary>Segura enquanto a tecla/botão estiver pressionado (solta = começa a sumir).</summary>
    public bool HoldWhilePressed { get; set; }
}

sealed class AttackLibrary
{
    public List<AttackPreset> Attacks { get; set; } = Defaults();

    public static string DefaultPath => Path.Combine(
        AppInfo.DataDir, "ataques.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static List<AttackPreset> Defaults() =>
    [
        new() { Name = "Flash branco", Type = AttackType.Flash, Color = "#FFFFFF", HoldMs = 60, ReleaseMs = 600, Key = "1" },
        new() { Name = "Blackout", Type = AttackType.Flash, Color = "#000000", AttackMs = 40, HoldMs = 150, ReleaseMs = 700, Key = "2" },
        new() { Name = "Strobe", Type = AttackType.Strobe, Color = "#FFFFFF", HoldMs = 700, ReleaseMs = 100, Count = 8, Key = "3", HoldWhilePressed = true },
        new() { Name = "Soco de zoom", Type = AttackType.Zoom, Color = "#FFFFFF", AttackMs = 30, HoldMs = 40, ReleaseMs = 400, Amount = 0.15f, Intensity = 0.25f, Key = "4" },
        new() { Name = "Tremor", Type = AttackType.Shake, HoldMs = 250, ReleaseMs = 300, Amount = 0.03f, Key = "5" },
        new() { Name = "Inverter", Type = AttackType.Invert, HoldMs = 80, ReleaseMs = 350, Key = "6" },
        new() { Name = "Onda de cor", Type = AttackType.Ring, Color = "#00E5FF", HoldMs = 0, ReleaseMs = 900, Amount = 0.12f, Key = "7" },
        new() { Name = "Varredura", Type = AttackType.Sweep, Color = "#FFC400", HoldMs = 0, ReleaseMs = 700, Amount = 0.15f, Key = "8" },
    ];

    public static AttackLibrary LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<AttackLibrary>(File.ReadAllText(path), JsonOptions) is { } lib)
                return lib;
        }
        catch { }
        return new AttackLibrary();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
