using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VJApp.Engine;

sealed record EffectParam(string Id, string Label, float Min, float Max, float Default, float Step);

/// <summary>
/// Um efeito é um arquivo .hlsl em Shaders/Effects com um cabeçalho de comentários:
/// <code>
/// // @name Blur
/// // @param amount "Quantidade" 0 1 0.3        (id, rótulo, mín, máx, padrão[, passo])
/// // @feedback                                  (opcional: recebe o quadro anterior em hist())
/// float4 Effect(float2 uv) { return tex(uv); }
/// </code>
/// </summary>
sealed record EffectDef(string Id, string Name, IReadOnlyList<EffectParam> Params, bool Feedback, string Body, string FilePath)
{
    public const int MaxParams = 8;

    static readonly string[] Components = ["x", "y", "z", "w"];

    /// <summary>Código HLSL completo: prelúdio comum + parâmetros como #define + corpo do efeito.</summary>
    public string BuildSource()
    {
        var sb = new StringBuilder(Prelude);
        for (int i = 0; i < Params.Count; i++)
            sb.AppendLine($"#define {Params[i].Id} (EP[{i / 4}].{Components[i % 4]})");
        sb.AppendLine($"#line 1 \"{Path.GetFileName(FilePath)}\"");
        sb.AppendLine(Body);
        sb.AppendLine("float4 PSMain(VSOut i) : SV_Target { return Effect(i.uv); }");
        return sb.ToString();
    }

    const string Prelude = """
        cbuffer Frame : register(b0)
        {
            float2 Resolution;
            float  Time;
            float  Speed;
            float  Brightness;
            float  OutputMode;
            float2 CompSize;
            float  Beat;
            float  Bpm;
            float2 _framePad;
        };
        cbuffer EffectParams : register(b2) { float4 EP[2]; };
        Texture2D Input   : register(t0);
        Texture2D History : register(t1);
        SamplerState LinearClamp : register(s0);
        struct VSOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };
        float4 tex(float2 uv)  { return Input.SampleLevel(LinearClamp, uv, 0); }
        float4 hist(float2 uv) { return History.SampleLevel(LinearClamp, uv, 0); }
        float2 mirrorUV(float2 v) { return 1 - abs(1 - 2 * frac(v * 0.5)); }
        float  luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
        static const float PI = 3.14159265;

        """;
}

static class EffectLibrary
{
    static readonly Regex NameRx = new(@"^\s*//\s*@name\s+(.+?)\s*$", RegexOptions.Multiline);
    static readonly Regex ParamRx = new(
        @"^\s*//\s*@param\s+(\w+)\s+""([^""]+)""\s+(\S+)\s+(\S+)\s+(\S+)(?:\s+(\S+))?\s*$", RegexOptions.Multiline);
    static readonly Regex FeedbackRx = new(@"^\s*//\s*@feedback\b", RegexOptions.Multiline);

    /// <summary>
    /// Pasta dos efeitos. Rodando do bin/ de desenvolvimento, usa a pasta do projeto,
    /// assim editar um .hlsl recarrega o efeito na hora sem recompilar o app.
    /// </summary>
    public static string FindDirectory()
    {
        var dev = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Shaders", "Effects"));
        return Directory.Exists(dev) ? dev : Path.Combine(AppContext.BaseDirectory, "Shaders", "Effects");
    }

    public static List<EffectDef> Load(string dir, List<string> errors)
    {
        var list = new List<EffectDef>();
        if (!Directory.Exists(dir)) return list;
        foreach (var file in Directory.GetFiles(dir, "*.hlsl").Order())
        {
            try
            {
                string text = File.ReadAllText(file);
                string id = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                var nameMatch = NameRx.Match(text);
                var ps = ParamRx.Matches(text).Take(EffectDef.MaxParams).Select(m => new EffectParam(
                    m.Groups[1].Value, m.Groups[2].Value, F(m.Groups[3].Value), F(m.Groups[4].Value), F(m.Groups[5].Value),
                    m.Groups[6].Success ? F(m.Groups[6].Value) : 0)).ToList();
                list.Add(new EffectDef(id, nameMatch.Success ? nameMatch.Groups[1].Value : id, ps,
                    FeedbackRx.IsMatch(text), text, file));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return list;
    }

    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>Instância de efeito como o render thread enxerga (valores na ordem dos parâmetros).</summary>
sealed class EffectRun(string effectId, object key)
{
    public string EffectId { get; } = effectId;
    /// <summary>Identidade estável da instância (para guardar o histórico do feedback).</summary>
    public object Key { get; } = key;
    public volatile bool Enabled = true;
    public readonly float[] Values = new float[EffectDef.MaxParams];
}
