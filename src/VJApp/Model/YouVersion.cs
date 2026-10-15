using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VJApp.Model;

/// <summary>
/// Versões da Bíblia pela YouVersion Platform API (api.youversion.com) com a chave de aplicação da pessoa.
/// Do PC fica guardada só a estrutura (livros, capítulos, número de versículos); o texto é buscado na hora,
/// capítulo por capítulo, e fica só na memória enquanto o programa está aberto (regras de licença das versões).
/// </summary>
sealed class YouVersionClient
{
    const string Api = "https://api.youversion.com";
    public const string Provider = "youversion";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static string KeyPath => Path.Combine(BibleStore.Dir, "youversion.key");

    string? _key = LoadKey();

    public bool HasKey => !string.IsNullOrEmpty(_key);

    /// <summary>Chave guardada criptografada só para o usuário do Windows (DPAPI).</summary>
    static string? LoadKey()
    {
        try
        {
            if (!File.Exists(KeyPath)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser));
        }
        catch { return null; }
    }

    public void SetKey(string? key)
    {
        _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        Directory.CreateDirectory(BibleStore.Dir);
        if (_key == null) { if (File.Exists(KeyPath)) File.Delete(KeyPath); return; }
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(_key), null, DataProtectionScope.CurrentUser));
    }

    async Task<JsonDocument> GetAsync(string pathAndQuery)
    {
        if (!HasKey) throw new InvalidOperationException("informe a chave da YouVersion em Versões… → YouVersion");
        using var req = new HttpRequestMessage(HttpMethod.Get, Api + pathAndQuery);
        req.Headers.Add("X-YVP-App-Key", _key);
        req.Headers.Add("X-YVP-Installation-Id", "vjapp-desktop");
        using var res = await Http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            string msg = res.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "a YouVersion recusou a chave (confira a chave e as permissões do app no portal)",
                System.Net.HttpStatusCode.NotFound => "não encontrado na YouVersion",
                (System.Net.HttpStatusCode)429 => "muitas consultas seguidas à YouVersion; tente de novo em instantes",
                _ => $"YouVersion respondeu {(int)res.StatusCode}{(body.Length > 0 && body.Length < 300 ? ": " + body : "")}",
            };
            throw new HttpRequestException(msg);
        }
        return JsonDocument.Parse(body);
    }

    static string S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static int I(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.TryParse(v.GetString(), out var n) ? n : 0
            : 0;

    /// <summary>Versões liberadas para a chave, no idioma pedido ("pt", "en", "es"... ou "*").</summary>
    public async Task<List<BibleVersionInfo>> VersionsAsync(string language)
    {
        var list = new List<BibleVersionInfo>();
        string? token = null;
        for (int page = 0; page < 20; page++)
        {
            var q = $"/v1/bibles?language_ranges[]={Uri.EscapeDataString(language)}&page_size=99" + (token != null ? $"&page_token={Uri.EscapeDataString(token)}" : "");
            using var doc = await GetAsync(q);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var v in data.EnumerateArray())
                {
                    int id = I(v, "id");
                    if (id == 0) continue;
                    var abbr = S(v, "localized_abbreviation") is { Length: > 0 } la ? la : S(v, "abbreviation");
                    var lang = S(v, "language_tag");
                    list.Add(new BibleVersionInfo
                    {
                        Id = $"yv_{id}",
                        Name = S(v, "localized_title") is { Length: > 0 } lt ? lt : S(v, "title"),
                        ShortName = abbr.Length > 0 ? abbr : id.ToString(),
                        Language = lang.Split('-')[0] switch { "pt" => "por", "en" => "eng", "es" => "spa", var x => x },
                        LanguageName = lang,
                        Books = v.TryGetProperty("books", out var b) && b.ValueKind == JsonValueKind.Array ? b.GetArrayLength() : 0,
                        PublicDomain = false,
                        LicenseUrl = S(v, "youversion_deep_link"),
                        Copyright = CopyrightText(v),
                        Provider = Provider,
                    });
                }
            token = S(root, "next_page_token");
            if (string.IsNullOrEmpty(token)) break;
        }
        return list;
    }

    static string CopyrightText(JsonElement v)
    {
        if (!v.TryGetProperty("copyright", out var c)) return "";
        var raw = c.ValueKind == JsonValueKind.String ? c.GetString() ?? ""
            : c.ValueKind == JsonValueKind.Object ? (S(c, "text") is { Length: > 0 } t ? t : S(c, "html")) : "";
        return Regex.Replace(System.Net.WebUtility.HtmlDecode(Regex.Replace(raw, "<[^>]+>", " ")), @"\s+", " ").Trim();
    }

    /// <summary>Estrutura da versão (livros, capítulos e quantos versículos cada um tem), sem o texto.</summary>
    public async Task<BibleText> IndexAsync(BibleVersionInfo info)
    {
        int id = int.Parse(info.Id[3..]);
        using var doc = await GetAsync($"/v1/bibles/{id}/index");
        var root = doc.RootElement;
        var text = new BibleText { Info = info };
        var books = root.TryGetProperty("books", out var bs) ? bs : root.TryGetProperty("data", out var d) ? d : default;
        if (books.ValueKind != JsonValueKind.Array) throw new InvalidDataException("índice da versão sem livros");
        var canonNames = BibleImport.Canon.ToDictionary(c => c.Id, c => c.Name);
        foreach (var b in books.EnumerateArray())
        {
            var bookId = S(b, "id").ToUpperInvariant();
            if (bookId.Length == 0) continue;
            var book = new BibleBook { Id = bookId, Name = S(b, "title") is { Length: > 0 } t ? t : canonNames.GetValueOrDefault(bookId, bookId) };
            if (b.TryGetProperty("chapters", out var chs) && chs.ValueKind == JsonValueKind.Array)
                foreach (var ch in chs.EnumerateArray())
                {
                    // ignora introduções (BOOK.INTRO) e afins: só capítulos numerados
                    var cid = S(ch, "id");
                    var last = cid.Split('.').Last();
                    if (ch.ValueKind == JsonValueKind.Object && cid.Length > 0 && !int.TryParse(last, out _)) continue;
                    int count = ch.ValueKind == JsonValueKind.Object && ch.TryGetProperty("verses", out var vs) && vs.ValueKind == JsonValueKind.Array
                        ? vs.EnumerateArray().Select(v => int.TryParse(S(v, "id").Split('.').Last(), out var n) ? n : 0).DefaultIfEmpty(0).Max()
                        : 0;
                    book.Chapters.Add([.. Enumerable.Repeat("", Math.Max(count, 0))]);
                }
            if (book.Chapters.Count > 0) text.Books.Add(book);
        }
        info.Books = text.Books.Count;
        return text;
    }

    /// <summary>Texto dos versículos de um capítulo (índice 0 = versículo 1).</summary>
    public async Task<List<string>> ChapterAsync(BibleVersionInfo info, string book, int chapter, int verseCount)
    {
        int id = int.Parse(info.Id[3..]);
        using var doc = await GetAsync($"/v1/bibles/{id}/passages/{book}.{chapter}?format=html&include_headings=false&include_notes=false");
        var html = S(doc.RootElement, "content");
        var verses = SplitVerses(html, book, chapter);
        if (verses.Count > 0) return verses;

        // sem marcas de versículo no HTML: busca um a um (em paralelo, poucos de cada vez)
        var result = new string[Math.Max(verseCount, 1)];
        using var gate = new SemaphoreSlim(6);
        await Task.WhenAll(Enumerable.Range(1, result.Length).Select(async v =>
        {
            await gate.WaitAsync();
            try
            {
                using var d = await GetAsync($"/v1/bibles/{id}/passages/{book}.{chapter}.{v}?format=text&include_headings=false&include_notes=false");
                result[v - 1] = Clean(S(d.RootElement, "content"), v);
            }
            catch (HttpRequestException) { result[v - 1] = ""; }
            finally { gate.Release(); }
        }));
        return [.. result];
    }

    /// <summary>Separa o HTML do capítulo nos versículos pelas marcas de número (data-usfm, v="N", rótulo do versículo).</summary>
    static List<string> SplitVerses(string html, string book, int chapter)
    {
        if (string.IsNullOrEmpty(html)) return [];
        // tira cabeçalhos e notas que eventualmente venham
        html = Regex.Replace(html, @"<(div|span)[^>]*class=""[^""]*\b(heading|note|footnote|s1|s2|r|ft|fr|x)\b[^""]*""[^>]*>.*?</\1>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var markers = new List<(int Pos, int Verse, int Len)>();
        var esc = Regex.Escape($"{book}.{chapter}.");
        foreach (var re in new[]
        {
            $@"<[^>]*data-usfm=""{esc}(\d+)[^""]*""[^>]*>",
            @"<[^>]*\bdata-(?:verse|v)=""(\d+)""[^>]*>",
            @"<[^>]*\sv=""(\d+)""[^>]*>",
            @"<[^>]*class=""[^""]*\b(?:v|verse-num|verse_number|yv-vlbl|label)\b[^""]*""[^>]*>\s*(\d+)\s*</[^>]+>",
        })
        {
            foreach (Match m in Regex.Matches(html, re, RegexOptions.IgnoreCase))
                markers.Add((m.Index, int.Parse(m.Groups[1].Value), m.Length));
            if (markers.Select(x => x.Verse).Distinct().Count() > 1) break;
            markers.Clear();
        }
        if (markers.Count == 0) return [];
        markers = [.. markers.OrderBy(m => m.Pos)];
        var verses = new SortedDictionary<int, string>();
        for (int i = 0; i < markers.Count; i++)
        {
            int start = markers[i].Pos + markers[i].Len;
            int end = i + 1 < markers.Count ? markers[i + 1].Pos : html.Length;
            if (end <= start) continue;
            var t = Clean(html[start..end], markers[i].Verse);
            if (t.Length == 0) continue;
            verses[markers[i].Verse] = verses.TryGetValue(markers[i].Verse, out var old) ? old + " " + t : t;
        }
        if (verses.Count == 0) return [];
        var list = new List<string>();
        for (int v = 1; v <= verses.Keys.Max(); v++) list.Add(verses.GetValueOrDefault(v, ""));
        return list;
    }

    /// <summary>Texto puro, sem o número do versículo no começo.</summary>
    static string Clean(string s, int verse)
    {
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = System.Net.WebUtility.HtmlDecode(s).Replace("¶", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, $@"^{verse}\s+", "");
        return s.Replace('<', '‹').Replace('>', '›');
    }
}
