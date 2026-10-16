using System.Drawing;
using System.Drawing.Text;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;

using VJApp;

namespace VJApp.Engine;

/// <summary>
/// Fontes do VJesus (além das instaladas no Windows): arquivos .ttf/.otf em %AppData%\VJesus\fontes,
/// importados de arquivo/zip ou baixados do Google Fonts pelo nome. Não precisa instalar no Windows.
/// </summary>
static class FontStore
{
    public static string Dir => Path.Combine(AppInfo.DataDir, "fontes");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    static readonly object Lock = new();
    /// <summary>
    /// Uma coleção por família: famílias com muitos pesos (ex.: Poppins Thin…Black) misturadas numa coleção só
    /// confundem o GDI+; separadas, cada uma tem seu normal/negrito/itálico certo.
    /// </summary>
    static Dictionary<string, PrivateFontCollection>? _families;
    /// <summary>Arquivo próprio de cada estilo (ex.: Poppins Bold = Poppins-700.ttf), para o negrito/itálico de verdade.</summary>
    static Dictionary<(string Name, bool Bold, bool Italic), PrivateFontCollection> _faces = [];
    static readonly List<PrivateFontCollection> _old = []; // coleções antigas ficam vivas (podem estar em uso no desenho)

    /// <summary>Arquivos de fonte de cada família.</summary>
    public static List<(string Family, string File, bool Bold, bool Italic)> Files { get; private set; } = [];

    public static void Reload()
    {
        lock (Lock)
        {
            if (_families != null) _old.AddRange(_families.Values);
            var loaded = new List<(string Name, string File, IntPtr Mem, int Len)>();
            if (Directory.Exists(Dir))
                foreach (var f in Directory.EnumerateFiles(Dir, "*.*", SearchOption.AllDirectories)
                             .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        // carrega da memória (não deixa o arquivo preso) e descobre o nome da família
                        var bytes = File.ReadAllBytes(f);
                        var mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(bytes.Length);
                        System.Runtime.InteropServices.Marshal.Copy(bytes, 0, mem, bytes.Length);
                        using var one = new PrivateFontCollection();
                        one.AddMemoryFont(mem, bytes.Length);
                        if (one.Families.FirstOrDefault()?.Name is { } name) loaded.Add((name, f, mem, bytes.Length));
                    }
                    catch { /* arquivo inválido: ignora */ }
                }
            var families = new Dictionary<string, PrivateFontCollection>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in loaded.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var col = new PrivateFontCollection();
                foreach (var x in g) col.AddMemoryFont(x.Mem, x.Len);
                families[g.Key] = col;
            }
            var faces = new Dictionary<(string, bool, bool), PrivateFontCollection>();
            foreach (var x in loaded)
            {
                var (bold, italic) = StyleOf(x.Mem, x.Len);
                var key = (x.Name.ToLowerInvariant(), bold, italic);
                if (faces.ContainsKey(key)) continue;
                var one = new PrivateFontCollection();
                one.AddMemoryFont(x.Mem, x.Len);
                faces[key] = one;
            }
            _old.AddRange(_faces.Values);
            _faces = faces;
            _families = families;
            Files = [.. loaded.Select(x => { var (b, i) = StyleOf(x.Mem, x.Len); return (x.Name, x.File, b, i); })];
        }
    }

    /// <summary>Negrito e itálico pela tabela OS/2 do arquivo (peso ≥ 600 ou bit de negrito; bit de itálico).</summary>
    static (bool Bold, bool Italic) StyleOf(IntPtr mem, int len)
    {
        try
        {
            var b = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(mem, b, 0, len);
            int U16(int o) => (b[o] << 8) | b[o + 1];
            long U32(int o) => ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3];
            int tables = U16(4);
            for (int i = 0; i < tables; i++)
            {
                int rec = 12 + i * 16;
                if (b[rec] == (byte)'O' && b[rec + 1] == (byte)'S' && b[rec + 2] == (byte)'/' && b[rec + 3] == (byte)'2')
                {
                    int off = (int)U32(rec + 8);
                    int weight = U16(off + 4), sel = U16(off + 62);
                    return (weight >= 600 || (sel & 0x20) != 0, (sel & 0x01) != 0);
                }
            }
        }
        catch { /* sem tabela OS/2: trata como normal */ }
        return (false, false);
    }

    /// <summary>
    /// Face para negrito/itálico: o arquivo próprio do estilo, se a fonte importada tiver (o GDI+ sozinho
    /// faria um negrito "falso" a partir do normal). Senão, a mesma família com o estilo sintético.
    /// </summary>
    public static (FontFamily Family, FontStyle Style) Face(FontFamily family, FontStyle wanted)
    {
        bool bold = wanted.HasFlag(FontStyle.Bold), italic = wanted.HasFlag(FontStyle.Italic);
        if (bold || italic)
            lock (Lock)
                if (_faces.TryGetValue((family.Name.ToLowerInvariant(), bold, italic), out var col) && col.Families.FirstOrDefault() is { } f)
                    return (f, wanted & FontStyle.Underline);
        return (family, wanted);
    }

    /// <summary>Famílias importadas (nomes como aparecem na lista de fontes).</summary>
    public static IEnumerable<string> Families
    {
        get { lock (Lock) { if (_families == null) Reload(); return [.. _families!.Keys]; } }
    }

    /// <summary>
    /// Família pelo nome: primeiro as importadas, depois as instaladas no Windows.
    /// Owned = true quando quem chamou deve dar Dispose (as importadas pertencem à coleção).
    /// </summary>
    public static (FontFamily Family, bool Owned) Find(string name)
    {
        lock (Lock)
        {
            if (_families == null) Reload();
            if (_families!.TryGetValue(name, out var col) && col.Families.FirstOrDefault() is { } f) return (f, false);
        }
        try { return (new FontFamily(name), true); }
        catch { return (FontFamily.GenericSansSerif, false); }
    }

    static readonly Dictionary<string, FontFamily> _installed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Família para usar sem Dispose (trechos com &lt;fonte&gt;): importada ou do Windows, guardada para reaproveitar.</summary>
    public static FontFamily Shared(string name)
    {
        var (f, owned) = Find(name);
        if (!owned) return f;
        lock (Lock)
        {
            if (_installed.TryGetValue(name, out var cached)) { f.Dispose(); return cached; }
            _installed[name] = f;
            return f;
        }
    }

    /// <summary>Copia .ttf/.otf (ou o conteúdo de um .zip) para a pasta de fontes. Retorna quantos arquivos entraram.</summary>
    public static int Import(IEnumerable<string> paths)
    {
        Directory.CreateDirectory(Dir);
        int count = 0;
        foreach (var p in paths)
        {
            var ext = Path.GetExtension(p).ToLowerInvariant();
            if (ext is ".ttf" or ".otf")
            {
                File.Copy(p, Path.Combine(Dir, Path.GetFileName(p)), overwrite: true);
                count++;
            }
            else if (ext == ".zip")
            {
                using var zip = ZipFile.OpenRead(p);
                foreach (var e in zip.Entries.Where(e => e.Name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                {
                    e.ExtractToFile(Path.Combine(Dir, e.Name), overwrite: true);
                    count++;
                }
            }
        }
        Reload();
        return count;
    }

    /// <summary>
    /// Baixa uma família do Google Fonts pelo nome (ex.: "Poppins"): normal, negrito, itálico e os outros pesos
    /// em .ttf. Usa a API pública de CSS do Google Fonts, que entrega TrueType para navegadores antigos.
    /// </summary>
    public static async Task<int> DownloadGoogleAsync(string family)
    {
        family = Regex.Replace(family.Trim(), @"\s+", " ");
        if (family.Length == 0) throw new ArgumentException("informe o nome da fonte");
        var name = Uri.EscapeDataString(family).Replace("%20", "+");
        string[] queries =
        [
            $"family={name}:ital,wght@0,100;0,200;0,300;0,400;0,500;0,600;0,700;0,800;0,900;1,100;1,200;1,300;1,400;1,500;1,600;1,700;1,800;1,900",
            $"family={name}:ital,wght@0,400;0,700;1,400;1,700",
            $"family={name}:wght@100;200;300;400;500;600;700;800;900",
            $"family={name}:wght@400;700",
            $"family={name}",
        ];
        string? css = null;
        foreach (var q in queries)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://fonts.googleapis.com/css2?{q}");
            // agente simples (como o wget): o Google responde com .ttf completo (que o GDI+ abre) em vez de woff/woff2
            req.Headers.TryAddWithoutValidation("User-Agent", "Wget/1.21");
            using var res = await Http.SendAsync(req);
            if (res.IsSuccessStatusCode) { css = await res.Content.ReadAsStringAsync(); break; }
        }
        if (css == null) throw new InvalidOperationException($"não encontrei \"{family}\" no Google Fonts (confira o nome como aparece no site)");

        var dir = Path.Combine(Dir, Regex.Replace(family, @"[^\w\- ]", ""));
        Directory.CreateDirectory(dir);
        int count = 0;
        foreach (Match block in Regex.Matches(css, @"@font-face\s*\{(.*?)\}", RegexOptions.Singleline))
        {
            var body = block.Groups[1].Value;
            var url = Regex.Match(body, @"url\((https://[^)]+)\)").Groups[1].Value;
            if (url.Length == 0) continue;
            var weight = Regex.Match(body, @"font-weight:\s*(\d+)").Groups[1].Value;
            var italic = Regex.IsMatch(body, @"font-style:\s*italic");
            var ext = url.Contains(".otf") ? ".otf" : ".ttf";
            var file = Path.Combine(dir, $"{family.Replace(" ", "")}-{(weight.Length > 0 ? weight : "400")}{(italic ? "i" : "")}{ext}");
            if (File.Exists(file) && IsFontFile(File.ReadAllBytes(file))) { count++; continue; }
            var bytes = await Http.GetByteArrayAsync(url);
            if (!IsFontFile(bytes)) continue; // woff/woff2 o GDI+ não abre
            await File.WriteAllBytesAsync(file, bytes);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("o Google Fonts não entregou arquivos para essa fonte");
        Reload();
        return count;
    }

    /// <summary>TrueType ou OpenType (o GDI+ não abre woff/woff2).</summary>
    static bool IsFontFile(byte[] b) => b.Length > 4 &&
        ((b[0] == 0 && b[1] == 1 && b[2] == 0 && b[3] == 0) || (b[0] == (byte)'O' && b[1] == (byte)'T' && b[2] == (byte)'T' && b[3] == (byte)'O') || (b[0] == (byte)'t' && b[1] == (byte)'r' && b[2] == (byte)'u' && b[3] == (byte)'e'));

    /// <summary>Apaga os arquivos de uma família importada.</summary>
    public static void Delete(string family)
    {
        List<string> files;
        lock (Lock) files = [.. Files.Where(f => f.Family.Equals(family, StringComparison.OrdinalIgnoreCase)).Select(f => f.File)];
        foreach (var f in files) try { File.Delete(f); } catch { /* em uso: fica para depois */ }
        Reload();
    }
}
