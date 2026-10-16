using System.Text.Json;

using VJApp;

namespace VJApp.Model;

/// <summary>Um item da tela de retorno: onde fica (0..1 da tela), se aparece e como é o texto.</summary>
sealed class StageElement
{
    public bool Visible { get; set; } = true;
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; } = 0.3f;
    public float H { get; set; } = 0.2f;
    /// <summary>Tamanho da fonte em % da altura da tela.</summary>
    public float FontSize { get; set; } = 5;
    public string Color { get; set; } = "#FFFFFF";
    /// <summary>"left", "center" ou "right".</summary>
    public string Align { get; set; } = "center";
}

/// <summary>
/// Tela de retorno (monitor virado para os músicos): slide atual, próximo, relógio, cronômetro,
/// alerta e mensagem — cada um com posição, tamanho e visibilidade próprios.
/// </summary>
sealed class StageConfig
{
    public bool Enabled { get; set; }
    public int Display { get; set; } = 1;
    public bool Fullscreen { get; set; } = true;
    public string Background { get; set; } = "#000000";
    public bool ClockSeconds { get; set; } = true;
    public string NextLabel { get; set; } = "PRÓXIMO";
    public bool ShowSongTitle { get; set; } = true;
    /// <summary>Quando a imagem do telão aparece, ela ocupa a tela toda (a letra some; relógio, cronômetro, alerta e mensagem ficam por cima).</summary>
    public bool ProgramFull { get; set; }

    public Dictionary<string, StageElement> Elements { get; set; } = DefaultElements();

    public static Dictionary<string, StageElement> DefaultElements() => new()
    {
        ["current"] = new() { X = 0.03f, Y = 0.12f, W = 0.94f, H = 0.46f, FontSize = 7.5f },
        ["next"] = new() { X = 0.03f, Y = 0.62f, W = 0.94f, H = 0.24f, FontSize = 4.5f, Color = "#9CC9FF" },
        ["clock"] = new() { X = 0.03f, Y = 0.01f, W = 0.3f, H = 0.1f, FontSize = 6, Align = "left" },
        ["timer"] = new() { X = 0.67f, Y = 0.01f, W = 0.3f, H = 0.1f, FontSize = 6, Color = "#FFD54F", Align = "right" },
        ["alert"] = new() { X = 0.15f, Y = 0.36f, W = 0.7f, H = 0.28f, FontSize = 8, Color = "#FFFFFF" },
        ["message"] = new() { X = 0.03f, Y = 0.88f, W = 0.94f, H = 0.1f, FontSize = 4, Color = "#80E27E" },
        // imagem do telão: só aparece quando há algo marcado para o retorno ou com "espelhar" ligado
        ["program"] = new() { X = 0.62f, Y = 0.6f, W = 0.35f, H = 0.35f, FontSize = 3 },
    };

    public static string DefaultPath => Path.Combine(
        AppInfo.DataDir, "retorno.json");

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static StageConfig LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<StageConfig>(File.ReadAllText(path), JsonOptions) is { } c)
            {
                // elementos novos que ainda não existiam no arquivo
                foreach (var (k, v) in DefaultElements()) c.Elements.TryAdd(k, v);
                return c;
            }
        }
        catch { }
        return new StageConfig();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}

/// <summary>Cronômetro/contagem regressiva da tela de retorno (a UI calcula o tempo pelo relógio do PC).</summary>
sealed class StageTimer
{
    /// <summary>"stopwatch" (conta para cima) ou "countdown" (conta para baixo).</summary>
    public string Mode { get; set; } = "countdown";
    public double DurationSeconds { get; set; } = 300;
    public bool Running { get; set; }
    /// <summary>Quando começou a contar (ms desde 1970), se estiver rodando.</summary>
    public double StartedAt { get; set; }
    /// <summary>Tempo já contado antes da última pausa (s).</summary>
    public double Accumulated { get; set; }
}
