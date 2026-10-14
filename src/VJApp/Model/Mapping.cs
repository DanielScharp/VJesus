using System.Text.Json;

namespace VJApp.Model;

/// <summary>Ponto normalizado (0..1).</summary>
sealed class Pt
{
    public float X { get; set; }
    public float Y { get; set; }
    public Pt() { }
    public Pt(float x, float y) { X = x; Y = y; }
}

/// <summary>Retângulo normalizado (0..1) recortado da composição.</summary>
sealed class Rect01
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; } = 1;
    public float H { get; set; } = 1;
}

/// <summary>Largura (0..0.5 do slice) das bordas suaves para juntar projetores, e a curva.</summary>
sealed class SoftEdge
{
    public float Left { get; set; }
    public float Right { get; set; }
    public float Top { get; set; }
    public float Bottom { get; set; }
    public float Gamma { get; set; } = 1;
}

/// <summary>
/// Um slice pega um retângulo da composição (Input) e desenha numa área deformável da tela (Points).
/// Points é uma grade de controle Cols × Rows (linha a linha). 2×2 = 4 cantos com perspectiva.
/// </summary>
sealed class Slice
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Slice";
    public bool Enabled { get; set; } = true;
    public float Opacity { get; set; } = 1;
    public Rect01 Input { get; set; } = new();
    public int Cols { get; set; } = 2;
    public int Rows { get; set; } = 2;
    public List<Pt> Points { get; set; } = DefaultPoints(2, 2);
    public SoftEdge SoftEdge { get; set; } = new();

    public static List<Pt> DefaultPoints(int cols, int rows, float margin = 0)
    {
        var list = new List<Pt>();
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                list.Add(new Pt(margin + (1 - 2 * margin) * c / (cols - 1), margin + (1 - 2 * margin) * r / (rows - 1)));
        return list;
    }

    public bool IsValid() => Cols >= 2 && Rows >= 2 && Points.Count == Cols * Rows;
}

/// <summary>Polígono que apaga (pinta de preto) uma área da saída.</summary>
sealed class Mask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Máscara";
    public bool Enabled { get; set; } = true;
    public List<Pt> Points { get; set; } = [new(0.4f, 0.4f), new(0.6f, 0.4f), new(0.6f, 0.6f), new(0.4f, 0.6f)];
}

/// <summary>Uma tela de saída (projetor/monitor): uma janela com seus slices e máscaras.</summary>
sealed class OutputScreen
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Tela";
    public int Display { get; set; }
    public bool Fullscreen { get; set; }
    public List<Slice> Slices { get; set; } = [];
    public List<Mask> Masks { get; set; } = [];
}

sealed class MappingSetup
{
    public List<OutputScreen> Screens { get; set; } = [];

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp", "mapeamento.json");

    public static MappingSetup CreateDefault() => new()
    {
        Screens = [new OutputScreen { Name = "Tela 1", Slices = [new Slice { Name = "Slice 1" }] }],
    };

    public static MappingSetup LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<MappingSetup>(File.ReadAllText(path), JsonOptions) is { Screens.Count: > 0 } m)
                return m;
        }
        catch { /* arquivo corrompido: começa do zero */ }
        return CreateDefault();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
