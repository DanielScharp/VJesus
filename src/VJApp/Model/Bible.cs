using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VJApp.Model;

/// <summary>Uma versão da Bíblia disponível para baixar (catálogo da Free Use Bible API, bible.helloao.org).</summary>
sealed class BibleVersionInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public string Language { get; set; } = "";
    public string LanguageName { get; set; } = "";
    public int Books { get; set; }
    public int Verses { get; set; }
    public bool PublicDomain { get; set; }
    public string LicenseUrl { get; set; } = "";
}

/// <summary>Livro: Chapters[c][v] = texto do versículo v+1 do capítulo c+1 ("" quando a versão não tem).</summary>
sealed class BibleBook
{
    public string Id { get; set; } = "";   // código padrão (GEN, EXO, ... REV): igual em todas as versões
    public string Name { get; set; } = "";
    public List<List<string>> Chapters { get; set; } = [];
}

sealed class BibleText
{
    public BibleVersionInfo Info { get; set; } = new();
    public List<BibleBook> Books { get; set; } = [];

    [JsonIgnore] Dictionary<string, BibleBook>? _byId;
    [JsonIgnore] public Dictionary<string, BibleBook> ById => _byId ??= Books.ToDictionary(b => b.Id);

    /// <summary>Texto sem acentos e em minúsculas, para a pesquisa por palavras (montado na primeira busca).</summary>
    [JsonIgnore] public List<(BibleBook Book, int Chapter, int Verse, string Folded)>? SearchIndex;

    public string? Verse(string book, int chapter, int verse) =>
        ById.TryGetValue(book, out var b) && chapter >= 1 && chapter <= b.Chapters.Count
        && verse >= 1 && verse <= b.Chapters[chapter - 1].Count ? b.Chapters[chapter - 1][verse - 1] : null;
}

/// <summary>Como a Bíblia aparece no telão e quais versões o operador usa.</summary>
sealed class BibleSettings
{
    public string? MainVersion { get; set; }
    /// <summary>Versões mostradas lado a lado no painel Comparar.</summary>
    public List<string> Compare { get; set; } = [];
    /// <summary>Versões que vão juntas para o telão (vazio = só a principal).</summary>
    public List<string> OnScreen { get; set; } = [];
    /// <summary>Cena das letras usada para a Bíblia (null = estilo padrão).</summary>
    public string? SceneId { get; set; }
    /// <summary>"abaixo", "acima" ou "nenhuma".</summary>
    public string Reference { get; set; } = "abaixo";
    public bool ShowVersion { get; set; } = true;
    public float ReferenceScale { get; set; } = 0.55f;
    public string ReferenceColor { get; set; } = "#FFD54F";
    /// <summary>Últimas passagens projetadas (mais recente primeiro).</summary>
    public List<string> History { get; set; } = [];
}

/// <summary>Versões baixadas em %AppData%\VJApp\biblia (funciona sem internet depois de baixar).</summary>
sealed class BibleStore
{
    const string Api = "https://bible.helloao.org";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp", "biblia");
    static string SettingsPath => Path.Combine(Dir, "config.json");
    static string CatalogPath => Path.Combine(Dir, "catalogo.json");
    static string VersionPath(string id) => Path.Combine(Dir, string.Concat(id.Split(Path.GetInvalidFileNameChars())) + ".json");

    readonly Dictionary<string, BibleText> _loaded = [];

    public BibleSettings Settings { get; set; } = Load<BibleSettings>(SettingsPath) ?? new();

    public void SaveSettings()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    static T? Load<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts) : default; }
        catch { return default; }
    }

    /// <summary>Versões já baixadas (lê só o cabeçalho de cada arquivo).</summary>
    public List<BibleVersionInfo> Installed()
    {
        if (!Directory.Exists(Dir)) return [];
        var list = new List<BibleVersionInfo>();
        foreach (var f in Directory.GetFiles(Dir, "*.json"))
        {
            if (f == SettingsPath || f == CatalogPath) continue;
            var id = Path.GetFileNameWithoutExtension(f);
            if (_loaded.TryGetValue(id, out var t)) { list.Add(t.Info); continue; }
            try
            {
                using var s = File.OpenRead(f);
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("Info", out var info))
                    list.Add(info.Deserialize<BibleVersionInfo>(JsonOpts)!);
            }
            catch { /* arquivo incompleto: ignora */ }
        }
        return list.OrderBy(v => v.Language == "por" ? 0 : 1).ThenBy(v => v.Name).ToList();
    }

    public BibleText? Get(string? id)
    {
        if (id == null) return null;
        if (_loaded.TryGetValue(id, out var t)) return t;
        t = Load<BibleText>(VersionPath(id));
        if (t != null) _loaded[id] = t;
        return t;
    }

    public void Delete(string id)
    {
        _loaded.Remove(id);
        if (File.Exists(VersionPath(id))) File.Delete(VersionPath(id));
    }

    /// <summary>Guarda uma versão importada de arquivo junto com as baixadas.</summary>
    public void Save(BibleText text)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(VersionPath(text.Info.Id), JsonSerializer.Serialize(text));
        _loaded[text.Info.Id] = text;
    }

    /// <summary>Lista de todas as versões livres (baixa da internet; sem conexão usa a última lista guardada).</summary>
    public async Task<List<BibleVersionInfo>> CatalogAsync()
    {
        try
        {
            var json = await Http.GetStringAsync($"{Api}/api/available_translations.json");
            Directory.CreateDirectory(Dir);
            await File.WriteAllTextAsync(CatalogPath, json);
            return ParseCatalog(json);
        }
        catch when (File.Exists(CatalogPath))
        {
            return ParseCatalog(await File.ReadAllTextAsync(CatalogPath));
        }
    }

    static List<BibleVersionInfo> ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<BibleVersionInfo>();
        foreach (var t in doc.RootElement.GetProperty("translations").EnumerateArray())
            list.Add(ParseInfo(t));
        return list;
    }

    static BibleVersionInfo ParseInfo(JsonElement t)
    {
        string S(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        int I(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
        bool pd = t.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object
            && lic.TryGetProperty("publicDomain", out var p) && p.ValueKind == JsonValueKind.True;
        return new BibleVersionInfo
        {
            Id = S("id"), Name = S("name"), ShortName = S("shortName"), Language = S("language"),
            LanguageName = S("languageName") is { Length: > 0 } ln ? ln : S("languageEnglishName"),
            Books = I("numberOfBooks"), Verses = I("totalNumberOfVerses"), PublicDomain = pd, LicenseUrl = S("licenseUrl"),
        };
    }

    /// <summary>Baixa a versão inteira e guarda num formato compacto (só o texto dos versículos).</summary>
    public async Task<BibleVersionInfo> DownloadAsync(string id)
    {
        var json = await Http.GetStringAsync($"{Api}/api/{Uri.EscapeDataString(id)}/complete.simple.json");
        var text = await Task.Run(() => Convert(json));
        Directory.CreateDirectory(Dir);
        var tmp = VersionPath(id) + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(text));
        File.Move(tmp, VersionPath(id), overwrite: true);
        _loaded[id] = text;
        return text.Info;
    }

    static BibleText Convert(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new BibleText { Info = ParseInfo(root.GetProperty("translation")) };
        foreach (var b in root.GetProperty("books").EnumerateArray())
        {
            var book = new BibleBook
            {
                Id = b.GetProperty("id").GetString() ?? "",
                Name = b.TryGetProperty("commonName", out var cn) && cn.GetString() is { Length: > 0 } n ? n : b.GetProperty("name").GetString() ?? "",
            };
            foreach (var c in b.GetProperty("chapters").EnumerateArray())
            {
                var verses = new List<string>();
                foreach (var item in c.GetProperty("chapter").GetProperty("content").EnumerateArray())
                {
                    if (item.GetProperty("type").GetString() != "verse") continue;
                    int number = item.GetProperty("number").GetInt32();
                    var t = item.GetProperty("text");
                    string s = (t.ValueKind == JsonValueKind.String ? t.GetString() : t.ToString())?.Trim() ?? "";
                    s = s.Replace('<', '‹').Replace('>', '›').Replace("¶", "").Trim(); // sem marcas de parágrafo e sem confundir com as marcações do telão
                    while (verses.Count < number) verses.Add("");
                    verses[number - 1] = verses[number - 1].Length > 0 ? verses[number - 1] + " " + s : s;
                }
                book.Chapters.Add(verses);
            }
            result.Books.Add(book);
        }
        result.Info.Books = result.Books.Count;
        return result;
    }

    /// <summary>Só letras e números, sem acento, minúsculas, com um espaço entre as palavras e nas pontas.</summary>
    static string Words(string s)
    {
        var f = Lyrics.Fold(s);
        var sb = new System.Text.StringBuilder(f.Length + 2).Append(' ');
        foreach (var ch in f) sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return System.Text.RegularExpressions.Regex.Replace(sb.Append(' ').ToString(), " {2,}", " ");
    }

    /// <summary>
    /// Versículos com todas as palavras (cada uma no começo de uma palavra do texto, sem diferenciar
    /// acentos e maiúsculas). Os que têm a frase exatamente como foi digitada vêm primeiro.
    /// </summary>
    public (List<(BibleBook Book, int Chapter, int Verse, string Text)> Hits, int Total) Search(string version, string query, int max = 300)
    {
        var text = Get(version);
        var phrase = Words(query);
        var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Select(w => " " + w).ToArray();
        if (text == null || words.Length == 0) return ([], 0);
        if (text.SearchIndex == null)
        {
            var idx = new List<(BibleBook, int, int, string)>(32000);
            foreach (var b in text.Books)
                for (int c = 0; c < b.Chapters.Count; c++)
                    for (int v = 0; v < b.Chapters[c].Count; v++)
                        if (b.Chapters[c][v].Length > 0) idx.Add((b, c + 1, v + 1, Words(b.Chapters[c][v])));
            text.SearchIndex = idx;
        }
        var exact = new List<(BibleBook, int, int, string)>();
        var other = new List<(BibleBook, int, int, string)>();
        foreach (var (b, c, v, f) in text.SearchIndex)
        {
            if (!words.All(w => f.Contains(w, StringComparison.Ordinal))) continue;
            (f.Contains(phrase, StringComparison.Ordinal) ? exact : other).Add((b, c, v, b.Chapters[c - 1][v - 1]));
        }
        return ([.. exact.Concat(other).Take(max)], exact.Count + other.Count);
    }
}
