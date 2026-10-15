using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace VJApp.Model;

/// <summary>
/// Importa uma Bíblia de um arquivo que a pessoa já tem (comprada ou com licença de uso):
/// MySword (.bbl.mybible), MyBible (.SQLite3), Zefania XML, OSIS XML ou JSON (lista de livros com capítulos).
/// </summary>
static class BibleImport
{
    /// <summary>Os 66 livros na ordem protestante: código padrão e nome em português.</summary>
    public static readonly (string Id, string Name)[] Canon =
    [
        ("GEN", "Gênesis"), ("EXO", "Êxodo"), ("LEV", "Levítico"), ("NUM", "Números"), ("DEU", "Deuteronômio"),
        ("JOS", "Josué"), ("JDG", "Juízes"), ("RUT", "Rute"), ("1SA", "1 Samuel"), ("2SA", "2 Samuel"),
        ("1KI", "1 Reis"), ("2KI", "2 Reis"), ("1CH", "1 Crônicas"), ("2CH", "2 Crônicas"), ("EZR", "Esdras"),
        ("NEH", "Neemias"), ("EST", "Ester"), ("JOB", "Jó"), ("PSA", "Salmos"), ("PRO", "Provérbios"),
        ("ECC", "Eclesiastes"), ("SNG", "Cânticos"), ("ISA", "Isaías"), ("JER", "Jeremias"), ("LAM", "Lamentações"),
        ("EZK", "Ezequiel"), ("DAN", "Daniel"), ("HOS", "Oseias"), ("JOL", "Joel"), ("AMO", "Amós"),
        ("OBA", "Obadias"), ("JON", "Jonas"), ("MIC", "Miqueias"), ("NAM", "Naum"), ("HAB", "Habacuque"),
        ("ZEP", "Sofonias"), ("HAG", "Ageu"), ("ZEC", "Zacarias"), ("MAL", "Malaquias"),
        ("MAT", "Mateus"), ("MRK", "Marcos"), ("LUK", "Lucas"), ("JHN", "João"), ("ACT", "Atos"),
        ("ROM", "Romanos"), ("1CO", "1 Coríntios"), ("2CO", "2 Coríntios"), ("GAL", "Gálatas"), ("EPH", "Efésios"),
        ("PHP", "Filipenses"), ("COL", "Colossenses"), ("1TH", "1 Tessalonicenses"), ("2TH", "2 Tessalonicenses"),
        ("1TI", "1 Timóteo"), ("2TI", "2 Timóteo"), ("TIT", "Tito"), ("PHM", "Filemom"), ("HEB", "Hebreus"),
        ("JAS", "Tiago"), ("1PE", "1 Pedro"), ("2PE", "2 Pedro"), ("1JN", "1 João"), ("2JN", "2 João"),
        ("3JN", "3 João"), ("JUD", "Judas"), ("REV", "Apocalipse"),
    ];

    static readonly string[] OsisBooks =
    [
        "Gen", "Exod", "Lev", "Num", "Deut", "Josh", "Judg", "Ruth", "1Sam", "2Sam", "1Kgs", "2Kgs", "1Chr", "2Chr",
        "Ezra", "Neh", "Esth", "Job", "Ps", "Prov", "Eccl", "Song", "Isa", "Jer", "Lam", "Ezek", "Dan", "Hos", "Joel",
        "Amos", "Obad", "Jonah", "Mic", "Nah", "Hab", "Zeph", "Hag", "Zech", "Mal", "Matt", "Mark", "Luke", "John",
        "Acts", "Rom", "1Cor", "2Cor", "Gal", "Eph", "Phil", "Col", "1Thess", "2Thess", "1Tim", "2Tim", "Titus",
        "Phlm", "Heb", "Jas", "1Pet", "2Pet", "1John", "2John", "3John", "Jude", "Rev",
    ];

    /// <summary>(livro 1-66, capítulo, versículo, texto) → BibleText.</summary>
    sealed class Builder
    {
        readonly SortedDictionary<int, SortedDictionary<int, SortedDictionary<int, string>>> _b = [];
        public void Add(int book, int chapter, int verse, string? text)
        {
            if (book < 1 || book > 66 || chapter < 1 || verse < 1 || string.IsNullOrWhiteSpace(text)) return;
            var t = Clean(text);
            if (t.Length == 0) return;
            if (!_b.TryGetValue(book, out var chs)) _b[book] = chs = [];
            if (!chs.TryGetValue(chapter, out var vs)) chs[chapter] = vs = [];
            vs[verse] = vs.TryGetValue(verse, out var old) ? old + " " + t : t;
        }
        public int Count => _b.Values.Sum(c => c.Values.Sum(v => v.Count));
        public List<BibleBook> Books() => _b.Select(kv =>
        {
            var book = new BibleBook { Id = Canon[kv.Key - 1].Id, Name = Canon[kv.Key - 1].Name };
            int maxCh = kv.Value.Keys.Max();
            for (int c = 1; c <= maxCh; c++)
            {
                var list = new List<string>();
                if (kv.Value.TryGetValue(c, out var vs))
                    for (int v = 1; v <= vs.Keys.Max(); v++) list.Add(vs.GetValueOrDefault(v, ""));
                book.Chapters.Add(list);
            }
            return book;
        }).ToList();
    }

    /// <summary>Tira marcações (HTML, RTF do MySword, números Strong, notas) e deixa só o texto.</summary>
    static string Clean(string s)
    {
        s = Regex.Replace(s, @"<(RF|f)>.*?<(Rf|/f)>", "", RegexOptions.Singleline);      // notas de rodapé
        s = Regex.Replace(s, @"<(W[GH]\d+[^>]*|S>\d+</S|n>.*?</n)>", "", RegexOptions.Singleline); // Strong e notas
        s = Regex.Replace(s, @"<[^>]+>", "");
        s = Regex.Replace(s, @"\{\\[^}]*\}|\\[a-z]+\d* ?", "");
        s = System.Net.WebUtility.HtmlDecode(s).Replace("¶", "");
        s = s.Replace('<', '‹').Replace('>', '›');
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static BibleText Load(string path, string name, string shortName)
    {
        var b = new Builder();
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string lower = path.ToLowerInvariant();
        var head = new byte[16];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) fs.ReadAtLeast(head, 16, throwOnEndOfStream: false);
        bool sqlite = head.AsSpan().SequenceEqual("SQLite format 3\0"u8);
        if (sqlite) ReadSqlite(path, b);
        else if (ext is ".json") ReadJson(path, b);
        else if (ext is ".xml" or ".osis" || lower.EndsWith(".zefania")) ReadXml(path, b);
        else throw new InvalidDataException("formato não reconhecido (use MySword, MyBible, Zefania XML, OSIS XML ou JSON)");
        if (b.Count < 100) throw new InvalidDataException("não encontrei versículos neste arquivo");

        var id = "imp_" + Regex.Replace(Lyrics.Fold(shortName), "[^a-z0-9]+", "").Trim();
        var books = b.Books();
        return new BibleText
        {
            Info = new BibleVersionInfo
            {
                Id = id, Name = name, ShortName = shortName, Language = "por", LanguageName = "importada",
                Books = books.Count, Verses = b.Count, PublicDomain = false, LicenseUrl = path,
            },
            Books = books,
        };
    }

    static void ReadSqlite(string path, Builder b)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        db.Open();
        var tables = new List<string>();
        using (var t = db.CreateCommand())
        {
            t.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var r = t.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }
        using var cmd = db.CreateCommand();
        bool myBible = tables.Contains("verses", StringComparer.OrdinalIgnoreCase);
        if (myBible)
        {
            // MyBible: livros numerados 10, 20, 30... (ordem canônica)
            var numbers = new SortedSet<int>();
            cmd.CommandText = "SELECT DISTINCT book_number FROM verses";
            using (var r = cmd.ExecuteReader()) while (r.Read()) numbers.Add(r.GetInt32(0));
            var order = numbers.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i + 1);
            bool full = numbers.Count >= 66;
            cmd.CommandText = "SELECT book_number, chapter, verse, text FROM verses";
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                int n = rd.GetInt32(0);
                int book = MyBibleBook(n) ?? (full ? order[n] : 0);
                b.Add(book, rd.GetInt32(1), rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3));
            }
        }
        else if (tables.Contains("Bible", StringComparer.OrdinalIgnoreCase))
        {
            cmd.CommandText = "SELECT Book, Chapter, Verse, Scripture FROM Bible"; // MySword
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) b.Add(rd.GetInt32(0), rd.GetInt32(1), rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3));
        }
        else throw new InvalidDataException("banco SQLite sem tabela de versículos conhecida");
    }

    /// <summary>Números de livro do MyBible (10 = Gênesis ... 730 = Apocalipse).</summary>
    static int? MyBibleBook(int n)
    {
        int[] codes =
        [
            10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 190, 220, 230, 240, 250, 260, 290, 300,
            310, 330, 340, 350, 360, 370, 380, 390, 400, 410, 420, 430, 440, 450, 460, 470, 480, 490, 500, 510, 520, 530,
            540, 550, 560, 570, 580, 590, 600, 610, 620, 630, 640, 650, 660, 670, 680, 690, 700, 710, 720, 730,
        ];
        int i = Array.IndexOf(codes, n);
        return i >= 0 ? i + 1 : null;
    }

    static void ReadXml(string path, Builder b)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidDataException("XML vazio");
        if (root.Name.LocalName.Equals("XMLBIBLE", StringComparison.OrdinalIgnoreCase))
        {
            // Zefania: <BIBLEBOOK bnumber><CHAPTER cnumber><VERS vnumber>
            foreach (var book in root.Descendants().Where(e => e.Name.LocalName == "BIBLEBOOK"))
                foreach (var ch in book.Elements().Where(e => e.Name.LocalName == "CHAPTER"))
                    foreach (var v in ch.Elements().Where(e => e.Name.LocalName == "VERS"))
                        b.Add(Int(book, "bnumber"), Int(ch, "cnumber"), Int(v, "vnumber"),
                            string.Concat(v.Nodes().OfType<XText>().Select(t => t.Value)) is { Length: > 0 } direct ? direct : v.Value);
            return;
        }
        // OSIS: <verse osisID="Gen.1.1">texto</verse> (ou marcos sID/eID)
        foreach (var v in root.Descendants().Where(e => e.Name.LocalName == "verse"))
        {
            var osis = (string?)v.Attribute("osisID");
            if (osis == null) continue;
            var parts = osis.Split(' ')[0].Split('.');
            if (parts.Length < 3) continue;
            int book = Array.IndexOf(OsisBooks, parts[0]) + 1;
            string text = v.Value;
            if (string.IsNullOrWhiteSpace(text) && v.Attribute("sID") != null)
            {
                // marco de início: o texto vai até o próximo marco
                var sb = new System.Text.StringBuilder();
                for (var n = v.NextNode; n != null && !(n is XElement e && e.Name.LocalName == "verse"); n = n.NextNode)
                    sb.Append(n is XElement x ? x.Value : (n as XText)?.Value);
                text = sb.ToString();
            }
            b.Add(book, int.TryParse(parts[1], out var c) ? c : 0, int.TryParse(parts[2], out var vn) ? vn : 0, text);
        }
    }

    static int Int(XElement e, string attr) =>
        int.TryParse((string?)e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(attr, StringComparison.OrdinalIgnoreCase)), out var n) ? n : 0;

    static void ReadJson(string path, Builder b)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path).TrimStart('﻿'));
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("books", out var books)) root = books;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON sem lista de livros");
        int index = 0;
        foreach (var item in root.EnumerateArray())
        {
            index++;
            // [{ "abbrev": "gn", "chapters": [["v1", "v2"], ...] }, ...]
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("chapters", out var chs) && chs.ValueKind == JsonValueKind.Array)
            {
                int c = 0;
                foreach (var ch in chs.EnumerateArray())
                {
                    c++;
                    int v = 0;
                    if (ch.ValueKind == JsonValueKind.Array)
                        foreach (var verse in ch.EnumerateArray()) b.Add(index, c, ++v, verse.ValueKind == JsonValueKind.String ? verse.GetString() : verse.ToString());
                }
            }
            // [{ "book": 1, "chapter": 1, "verse": 1, "text": "..." }, ...]
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var text))
            {
                int Num(string n) => item.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0;
                b.Add(Num("book"), Num("chapter"), Num("verse"), text.GetString());
            }
        }
    }
}
