using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>Aba Bíblia: versões livres baixadas, navegação, pesquisa, comparação e projeção no telão.</summary>
sealed partial class MainForm
{
    readonly BibleStore _bible = new();

    /// <summary>Passagem no telão (null = não é Bíblia).</summary>
    sealed record BiblePassage(string Book, int Chapter, int Verse, int VerseEnd);
    BiblePassage? _liveBible;

    readonly HashSet<string> _bibleDownloading = [];

    void OnBibleMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "bibleHello":
                SendBibleState();
                break;
            case "bibleCatalog":
                _ = SendBibleCatalogAsync();
                break;
            case "bibleDownload":
                if ((string?)msg["id"] is { } id) _ = DownloadBibleAsync(id);
                break;
            case "bibleDelete":
                if ((string?)msg["id"] is { } del)
                {
                    _bible.Delete(del);
                    var s = _bible.Settings;
                    s.Compare.Remove(del);
                    s.OnScreen.Remove(del);
                    if (s.MainVersion == del) s.MainVersion = _bible.Installed().FirstOrDefault()?.Id;
                    _bible.SaveSettings();
                    SendBibleState();
                }
                break;
            case "bibleSettings":
                if (msg["settings"].Deserialize<BibleSettings>(JsonRead) is { } settings)
                {
                    settings.History = _bible.Settings.History;
                    _bible.Settings = settings;
                    _bible.SaveSettings();
                    SendBibleState();
                    if (_liveBible != null) ShowBible(_liveBible, addHistory: false);
                }
                break;
            case "bibleImport":
                ImportBible(((string?)msg["name"])?.Trim(), ((string?)msg["shortName"])?.Trim());
                break;
            case "bibleYvKey":
                _bible.YouVersion.SetKey((string?)msg["key"]);
                SendBibleState();
                if (_bible.YouVersion.HasKey) _ = SendYouVersionListAsync((string?)msg["language"] ?? "pt");
                break;
            case "bibleYvVersions":
                _ = SendYouVersionListAsync((string?)msg["language"] ?? "pt");
                break;
            case "bibleYvAdd":
                if ((string?)msg["id"] is { } yid && _yvList.FirstOrDefault(v => v.Id == yid) is { } yinfo) _ = AddYouVersionAsync(yinfo);
                break;
            case "bibleBooks":
                SendBibleBooks((string?)msg["version"]);
                break;
            case "bibleChapter":
            {
                var versions = msg["versions"]?.AsArray().Select(v => (string?)v).OfType<string>().Distinct().ToList() ?? [];
                string book = (string)msg["book"]!;
                int chapter = (int)msg["chapter"]!;
                _ = SendBibleChapterAsync(versions, book, chapter);
                break;
            }
            case "bibleSearch":
            {
                string version = (string?)msg["version"] ?? "";
                string query = (string?)msg["query"] ?? "";
                var (hits, total) = _bible.Search(version, query);
                Send(new
                {
                    type = "bibleSearch", query, total,
                    hits = hits.Select(h => new { book = h.Book.Id, bookName = h.Book.Name, chapter = h.Chapter, verse = h.Verse, text = h.Text }),
                });
                break;
            }
            case "bibleShow":
            {
                int verse = (int?)msg["verse"] ?? 1;
                _ = ShowBibleAsync(new BiblePassage((string)msg["book"]!, (int)msg["chapter"]!, verse, Math.Max(verse, (int?)msg["verseEnd"] ?? verse)));
                break;
            }
        }
    }

    async Task SendBibleCatalogAsync()
    {
        try
        {
            var list = await _bible.CatalogAsync();
            Send(new { type = "bibleCatalog", list });
        }
        catch (Exception ex)
        {
            Send(new { type = "bibleCatalog", list = Array.Empty<BibleVersionInfo>(), error = $"Sem acesso à lista de versões ({ex.Message}). Verifique a internet." });
        }
    }

    async Task DownloadBibleAsync(string id)
    {
        if (!_bibleDownloading.Add(id)) return;
        SendBibleState();
        try
        {
            var info = await _bible.DownloadAsync(id);
            var s = _bible.Settings;
            s.MainVersion ??= info.Id;
            if (!s.Compare.Contains(info.Id)) s.Compare.Add(info.Id);
            _bible.SaveSettings();
            Send(new { type = "notice", message = $"Bíblia baixada: {info.Name}" });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui baixar a versão {id}: {ex.Message}" });
        }
        finally
        {
            _bibleDownloading.Remove(id);
            SendBibleState();
        }
    }

    /// <summary>Bíblia de um arquivo que a pessoa já tem (ARA, NAA, NVI... com licença de uso).</summary>
    void ImportBible(string? name, string? shortName)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Importar Bíblia (MySword, MyBible, Zefania, OSIS ou JSON)",
            Filter = "Bíblias|*.mybible;*.bbl;*.sqlite3;*.sqlite;*.db;*.xml;*.osis;*.json|Todos os arquivos|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        string file = dlg.FileName;
        shortName = string.IsNullOrWhiteSpace(shortName) ? Path.GetFileNameWithoutExtension(file).Split('.')[0] : shortName;
        name = string.IsNullOrWhiteSpace(name) ? shortName : name;
        Send(new { type = "notice", message = $"Importando {Path.GetFileName(file)}…" });
        Task.Run(() => BibleImport.Load(file, name, shortName!)).ContinueWith(t => BeginInvoke(() =>
        {
            if (t.Exception != null)
            {
                Send(new { type = "error", message = $"Não consegui importar: {t.Exception.GetBaseException().Message}" });
                return;
            }
            var text = t.Result;
            _bible.Save(text);
            var s = _bible.Settings;
            s.MainVersion ??= text.Info.Id;
            if (!s.Compare.Contains(text.Info.Id)) s.Compare.Add(text.Info.Id);
            _bible.SaveSettings();
            Send(new { type = "notice", message = $"Bíblia importada: {text.Info.Name} ({text.Info.Books} livros, {text.Info.Verses} versículos)" });
            SendBibleState();
        }));
    }

    void SendBibleState()
    {
        var installed = _bible.Installed();
        var s = _bible.Settings;
        if (s.MainVersion == null || installed.All(v => v.Id != s.MainVersion)) s.MainVersion = installed.FirstOrDefault()?.Id;
        Send(new
        {
            type = "bibleState",
            installed,
            downloading = _bibleDownloading.ToArray(),
            settings = s,
            live = _liveBible,
            youVersionKey = _bible.YouVersion.HasKey,
        });
    }

    void SendBibleBooks(string? version)
    {
        var text = _bible.Get(version);
        Send(new
        {
            type = "bibleBooks",
            version,
            books = text?.Books.Select(b => new { id = b.Id, name = b.Name, chapters = b.Chapters.Select(c => c.Count) }) ?? [],
        });
    }

    /// <summary>Nome do livro na versão principal (ou na primeira que tiver o livro).</summary>
    string BookName(string book) =>
        _bible.Get(_bible.Settings.MainVersion)?.ById.GetValueOrDefault(book)?.Name
        ?? _bible.Installed().Select(v => _bible.Get(v.Id)?.ById.GetValueOrDefault(book)?.Name).FirstOrDefault(n => n != null)
        ?? book;

    static string RefText(string bookName, BiblePassage p) =>
        $"{bookName} {p.Chapter}:{p.Verse}{(p.VerseEnd > p.Verse ? "-" + p.VerseEnd : "")}";

    /// <summary>Texto da passagem numa versão (vários versículos com o número na frente).</summary>
    string? PassageText(string version, BiblePassage p)
    {
        var text = _bible.Get(version);
        if (text == null) return null;
        var sb = new StringBuilder();
        for (int v = p.Verse; v <= p.VerseEnd; v++)
        {
            var t = text.Verse(p.Book, p.Chapter, v);
            if (string.IsNullOrEmpty(t)) continue;
            if (sb.Length > 0) sb.Append(' ');
            if (p.VerseEnd > p.Verse) sb.Append($"<tam 60%>{v}</tam> ");
            sb.Append(t);
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Monta o texto do telão: a passagem em cada versão escolhida, com a referência.</summary>
    string? ComposeBible(BiblePassage p)
    {
        var s = _bible.Settings;
        var versions = s.OnScreen.Count > 0 ? s.OnScreen : s.MainVersion != null ? [s.MainVersion] : [];
        var blocks = new List<string>();
        var bookName = BookName(p.Book);
        float scale = Math.Clamp(s.ReferenceScale, 0.2f, 1.5f);
        foreach (var v in versions)
        {
            var body = PassageText(v, p);
            if (body == null) continue;
            var info = _bible.Get(v)!.Info;
            string reference = s.ShowVersion ? $"{RefText(bookName, p)} ({info.ShortName})" : RefText(bookName, p);
            string tag = $"<tam {Math.Round(scale * 100)}%><cor {s.ReferenceColor}>{reference}</cor></tam>";
            blocks.Add(s.Reference switch
            {
                "acima" => $"{tag}\n{body}",
                "nenhuma" => body,
                _ => $"{body}\n{tag}",
            });
        }
        return blocks.Count > 0 ? string.Join("\n", blocks) : null;
    }

    void ShowBible(BiblePassage p, bool addHistory = true)
    {
        var text = ComposeBible(p);
        if (text == null)
        {
            Send(new { type = "error", message = "Essa passagem não existe na versão escolhida." });
            return;
        }
        _liveSongId = null;
        _liveSlide = -1;
        _liveBible = p;
        _liveText = text;
        _blank = false;
        if (addHistory)
        {
            var h = _bible.Settings.History;
            var key = $"{p.Book} {p.Chapter}:{p.Verse}-{p.VerseEnd}";
            h.Remove(key);
            h.Insert(0, key);
            if (h.Count > 30) h.RemoveRange(30, h.Count - 30);
            _bible.SaveSettings();
        }
        UpdateLyricsOutput();
        SendBibleState();
    }

    /// <summary>Próximo/anterior versículo (mesmas teclas, OSC e celular das letras); passa para o capítulo seguinte.</summary>
    void StepBible(int direction)
    {
        if (_liveBible is not { } p) return;
        var text = _bible.Get(_bible.Settings.MainVersion);
        if (text?.ById.GetValueOrDefault(p.Book) is not { } book) return;
        int span = p.VerseEnd - p.Verse + 1;
        int chapter = p.Chapter, verse = direction > 0 ? p.VerseEnd + 1 : p.Verse - span;
        if (verse > book.Chapters[chapter - 1].Count)
        {
            if (chapter >= book.Chapters.Count) return;
            chapter++;
            verse = 1;
        }
        else if (verse < 1)
        {
            if (p.Verse > 1) verse = 1;
            else
            {
                if (chapter <= 1) return;
                chapter--;
                verse = Math.Max(1, book.Chapters[chapter - 1].Count - span + 1);
            }
        }
        int end = Math.Min(verse + span - 1, book.Chapters[chapter - 1].Count);
        _ = ShowBibleAsync(new BiblePassage(p.Book, chapter, verse, end));
    }

    /// <summary>Para a tela de retorno: referência e o próximo versículo.</summary>
    (string Title, string? Next) BibleStageInfo(BiblePassage p)
    {
        var main = _bible.Settings.MainVersion;
        string title = RefText(BookName(p.Book), p);
        string? next = main != null ? _bible.Get(main)?.Verse(p.Book, p.Chapter, p.VerseEnd + 1) : null;
        return (title, next);
    }

    LyricStyle BibleStyle() =>
        _lyrics.Scenes.FirstOrDefault(s => s.Id == _bible.Settings.SceneId)?.Style ?? _lyrics.Style;
}

/// <summary>Versões da YouVersion: texto buscado na hora, capítulo por capítulo.</summary>
sealed partial class MainForm
{
    List<BibleVersionInfo> _yvList = [];

    async Task SendYouVersionListAsync(string language)
    {
        try
        {
            _yvList = await _bible.YouVersion.VersionsAsync(language);
            Send(new { type = "bibleYvVersions", language, list = _yvList });
        }
        catch (Exception ex)
        {
            Send(new { type = "bibleYvVersions", language, list = Array.Empty<BibleVersionInfo>(), error = ex.Message });
        }
    }

    async Task AddYouVersionAsync(BibleVersionInfo info)
    {
        if (!_bibleDownloading.Add(info.Id)) return;
        SendBibleState();
        try
        {
            var added = await _bible.AddYouVersionAsync(info);
            var s = _bible.Settings;
            s.MainVersion ??= added.Id;
            if (!s.Compare.Contains(added.Id)) s.Compare.Add(added.Id);
            _bible.SaveSettings();
            Send(new { type = "notice", message = $"Versão adicionada pela YouVersion: {added.Name}" });
        }
        catch (Exception ex)
        {
            Send(new { type = "error", message = $"Não consegui adicionar {info.ShortName} da YouVersion: {ex.Message}" });
        }
        finally
        {
            _bibleDownloading.Remove(info.Id);
            SendBibleState();
        }
    }

    /// <summary>Busca nas versões online o que faltar do capítulo (avisando se der erro).</summary>
    async Task<bool> EnsureChapterAsync(IEnumerable<string> versions, string book, int chapter)
    {
        bool ok = true;
        foreach (var v in versions.Distinct())
        {
            try { await _bible.EnsureAsync(v, book, chapter); }
            catch (Exception ex)
            {
                ok = false;
                Send(new { type = "error", message = $"{_bible.Get(v)?.Info.ShortName ?? v}: {ex.Message}" });
            }
        }
        return ok;
    }

    async Task SendBibleChapterAsync(List<string> versions, string book, int chapter)
    {
        await EnsureChapterAsync(versions, book, chapter);
        Send(new
        {
            type = "bibleChapter",
            book, chapter,
            texts = versions.ToDictionary(v => v, v =>
                _bible.Get(v)?.ById.GetValueOrDefault(book) is { } b && chapter >= 1 && chapter <= b.Chapters.Count
                    ? b.Chapters[chapter - 1] : null),
        });
    }

    async Task ShowBibleAsync(BiblePassage p)
    {
        var s = _bible.Settings;
        var versions = (s.OnScreen.Count > 0 ? s.OnScreen : []).Append(s.MainVersion).OfType<string>().ToList();
        await EnsureChapterAsync(versions, p.Book, p.Chapter);
        ShowBible(p);
        // já deixa o próximo capítulo pronto para o → não esperar a internet
        if (_bible.Get(s.MainVersion)?.ById.GetValueOrDefault(p.Book) is { } b && p.Chapter < b.Chapters.Count)
            _ = EnsureChapterAsync(versions, p.Book, p.Chapter + 1);
    }
}
