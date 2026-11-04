using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Checagem antes do culto: confere tudo de uma vez (roteiro, arquivos, vídeos pesados, telas de saída,
/// apresentações, som, tela de pânico, espaço em disco) e diz o que resolver antes de começar.
/// Arquivos que mudaram de lugar podem ser encontrados de novo por aqui.
/// </summary>
sealed partial class MainForm
{
    sealed record CheckItem(string Level, string Area, string Title, string? Detail = null, string? Fix = null, string? FixLabel = null, string? Path = null);

    bool _checking;
    readonly Dictionary<string, (DateTime Time, (int Width, int Height, string Codec, double Fps)? Info)> _probeCache = [];

    void OnCheckMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "checkRun":
                _ = RunCheckAsync();
                break;
            case "checkRelink":
                RelinkOne((string?)msg["path"]);
                break;
            case "checkSearchFolder":
                RelinkFromFolder();
                break;
        }
    }

    async Task RunCheckAsync()
    {
        if (_checking) return;
        _checking = true;
        Send(new { type = "check", running = true, items = Array.Empty<object>() });
        try
        {
            _comp.StoreDeck();
            var clips = AllClips(_comp).Where(c => c != null).Select(c => c!).ToList();
            var plan = _service.Active;
            var items = new List<CheckItem>();

            CheckService(plan, items);
            CheckFiles(clips, items);
            CheckPresentations(clips, items);
            CheckOutputs(items);
            CheckSystem(items);
            // ler os cabeçalhos dos vídeos pode levar alguns segundos: fora da tela
            var videos = clips.Where(c => c.Kind == ClipKind.Video && c.Path is { } p && File.Exists(p) && !IsStillFile(p))
                .Select(c => c.Path!).Distinct(StringComparer.OrdinalIgnoreCase).Take(80).ToList();
            var probes = await Task.Run(() => videos.Select(p => (Path: p, Info: ProbeCached(p))).ToList());
            CheckVideos(probes, items);

            if (!items.Any(i => i.Area == "Roteiro")) items.Add(new("ok", "Roteiro", $"Roteiro \"{plan.Name}\" com {plan.Items.Count} itens, tudo encontrado."));
            if (!items.Any(i => i.Area == "Arquivos")) items.Add(new("ok", "Arquivos", $"Todos os {clips.Count(c => c.Path != null)} arquivos da grade estão no lugar."));
            if (!items.Any(i => i.Area == "Vídeos")) items.Add(new("ok", "Vídeos", $"{probes.Count} vídeo(s) conferidos: nenhum pesado demais para a configuração atual."));
            if (!items.Any(i => i.Area == "Saídas")) items.Add(new("ok", "Saídas", $"{_mapping.Screens.Count} tela(s) de saída em monitores conectados."));

            var order = new Dictionary<string, int> { ["erro"] = 0, ["aviso"] = 1, ["info"] = 2, ["ok"] = 3 };
            var sorted = items.OrderBy(i => order.GetValueOrDefault(i.Level, 9)).ToList();
            Send(new
            {
                type = "check",
                running = false,
                when = DateTime.Now.ToString("HH:mm"),
                errors = items.Count(i => i.Level == "erro"),
                warnings = items.Count(i => i.Level == "aviso"),
                items = sorted.Select(i => new { level = i.Level, area = i.Area, title = i.Title, detail = i.Detail, fix = i.Fix, fixLabel = i.FixLabel, path = i.Path }),
            });
        }
        catch (Exception ex)
        {
            Send(new { type = "check", running = false, items = new[] { new { level = "erro", area = "Checagem", title = "A checagem falhou: " + ex.Message } } });
        }
        finally { _checking = false; }
    }

    (int Width, int Height, string Codec, double Fps)? ProbeCached(string path)
    {
        var time = File.GetLastWriteTimeUtc(path);
        lock (_probeCache)
            if (_probeCache.TryGetValue(path, out var c) && c.Time == time) return c.Info;
        var info = VideoPlayer.Probe(path);
        lock (_probeCache) _probeCache[path] = (time, info);
        return info;
    }

    void CheckService(ServicePlan plan, List<CheckItem> items)
    {
        if (plan.Items.Count == 0)
        {
            items.Add(new("aviso", "Roteiro", $"O roteiro \"{plan.Name}\" está vazio.", "Monte a ordem do culto na aba Culto para passar tudo com uma tecla."));
            return;
        }
        int n = 0;
        foreach (var it in plan.Items)
        {
            n++;
            string where = $"Item {n} do roteiro" + (string.IsNullOrWhiteSpace(it.Title) ? "" : $" (\"{it.Title}\")");
            switch (it.Kind)
            {
                case "song":
                    if (_lyrics.Songs.All(s => s.Id != it.SongId))
                        items.Add(new("erro", "Roteiro", $"{where}: a música não está mais na biblioteca de letras.", "Escolha a música de novo no item ou apague o item."));
                    break;
                case "clip":
                case "column":
                {
                    var deck = _comp.Decks.FirstOrDefault(d => d.Id == it.DeckId) ?? (it.DeckId == null ? _comp.Current : null);
                    if (deck == null) { items.Add(new("erro", "Roteiro", $"{where}: a aba da grade foi apagada.", "Escolha a aba de novo no item.")); break; }
                    if (it.Kind == "clip")
                    {
                        var clip = it.Layer < deck.Clips.Count && it.Column >= 0 && it.Column < deck.Clips[it.Layer].Length ? deck.Clips[it.Layer][it.Column] : null;
                        if (clip == null) items.Add(new("erro", "Roteiro", $"{where}: a célula da grade está vazia (aba \"{deck.Name}\").", "O clip foi movido ou apagado; escolha de novo no item."));
                    }
                    else if (it.Column < 0 || it.Column >= deck.Columns || deck.Clips.All(r => it.Column >= r.Length || r[it.Column] == null))
                        items.Add(new("erro", "Roteiro", $"{where}: a coluna {it.Column + 1} da aba \"{deck.Name}\" está vazia.", "Escolha outra coluna no item."));
                    break;
                }
                case "text" or "notice" when string.IsNullOrWhiteSpace(it.Text):
                    items.Add(new("aviso", "Roteiro", $"{where}: o texto está vazio."));
                    break;
            }
        }
    }

    void CheckFiles(List<Clip> clips, List<CheckItem> items)
    {
        var missing = clips.Where(c => c.Kind is ClipKind.Video && c.Path is { } p && !File.Exists(p))
            .GroupBy(c => c.Path!, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var g in missing)
            items.Add(new("erro", "Arquivos", $"Arquivo não encontrado: {Path.GetFileName(g.Key)}",
                $"Estava em {Path.GetDirectoryName(g.Key)} ({g.Count()} clip(s) usam). Se a pasta mudou de lugar, aponte o arquivo de novo.",
                "relink", "Procurar arquivo…", g.Key));
        if (missing.Count > 1)
            items.Add(new("info", "Arquivos", $"{missing.Count} arquivos sumiram. Se estão todos numa pasta (ex.: HD externo com outra letra), procure todos de uma vez.",
                null, "searchFolder", "Procurar todos numa pasta…"));
    }

    void CheckPresentations(List<Clip> clips, List<CheckItem> items)
    {
        foreach (var c in clips.Where(c => c.Kind == ClipKind.Presentation).DistinctBy(c => c.Path))
        {
            bool ok = c.Path is { } dir && Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.png").Any();
            if (!ok)
                items.Add(new("erro", "Apresentações", $"A apresentação \"{c.Name}\" não está preparada (slides não encontrados).",
                    "Importe a apresentação de novo: botão direito na célula → Apresentação (PowerPoint ou PDF)."));
        }
    }

    void CheckOutputs(List<CheckItem> items)
    {
        var displays = Screen.AllScreens;
        if (displays.Length < 2)
            items.Add(new("aviso", "Saídas", "Só um monitor conectado.", "Confira se o projetor ou o painel está ligado e no modo \"Estender estas telas\" do Windows (tecla Windows + P)."));
        foreach (var s in _mapping.Screens)
        {
            if (s.Display >= displays.Length)
                items.Add(new("erro", "Saídas", $"A tela de saída \"{s.Name}\" está no monitor {s.Display + 1}, que não está conectado.",
                    "Ligue o projetor/painel ou escolha outro monitor em Menu → Mapeamento."));
            else if (s.Fullscreen && displays[s.Display].Primary)
                items.Add(new("aviso", "Saídas", $"A tela de saída \"{s.Name}\" está em tela cheia no monitor principal (o do operador).",
                    "Ela vai cobrir o VJesus. Escolha o monitor do projetor em Menu → Mapeamento."));
        }
        if (_mapping.Screens.Count == 0)
            items.Add(new("aviso", "Saídas", "Nenhuma tela de saída configurada.", "Crie uma em Menu → Mapeamento."));
    }

    void CheckVideos(List<(string Path, (int Width, int Height, string Codec, double Fps)? Info)> probes, List<CheckItem> items)
    {
        string mode = VideoPlayer.DecodeMode;
        foreach (var (path, info) in probes)
        {
            string name = Path.GetFileName(path);
            if (info is not { } v)
            {
                items.Add(new("erro", "Vídeos", $"Não consegui ler o vídeo {name}.", "O arquivo pode estar corrompido ou num formato que o VJesus não abre."));
                continue;
            }
            bool hap = v.Codec.StartsWith("hap", StringComparison.OrdinalIgnoreCase);
            long pixels = (long)v.Width * v.Height;
            string size = $"{v.Width}×{v.Height}, {v.Codec}, {Math.Round(v.Fps)} qps";
            if (pixels > 3840L * 2160)
                items.Add(new("aviso", "Vídeos", $"{name} é maior que 4K ({size}).", "Muito pesado para tocar ao vivo. Exporte em 4K ou Full HD."));
            else if (pixels > 1920L * 1088 && hap)
                items.Add(new("info", "Vídeos", $"{name} é HAP maior que Full HD ({size}).", "Toca leve, mas lê muito do disco. Prefira SSD."));
            else if (pixels > 1920L * 1088 && mode == "cpu")
                items.Add(new("aviso", "Vídeos", $"{name} é pesado ({size}) e os vídeos estão sendo abertos só pelo processador.",
                    "Em Menu → Desempenho do vídeo, escolha Automático para a placa de vídeo ajudar."));
            else if (pixels > 1920L * 1088 && v.Fps > 50)
                items.Add(new("info", "Vídeos", $"{name}: {size}.", "Vídeo grande com muitos quadros por segundo. Se travar, use Menu → Desempenho do vídeo → Testar este computador."));
        }
    }

    void CheckSystem(List<CheckItem> items)
    {
        if (_audioError != null) items.Add(new("aviso", "Som", _audioError, "Confira o dispositivo de som padrão do Windows."));
        if (_panic.Image == null) items.Add(new("info", "Tela de pânico", "A tela de pânico ainda não tem o logo da igreja.", "Clique na engrenagem ao lado do botão PÂNICO, no topo, para escolher."));
        else if (!File.Exists(_panic.Image)) items.Add(new("aviso", "Tela de pânico", "A imagem da tela de pânico não foi encontrada.", "Escolha de novo na engrenagem ao lado do botão PÂNICO."));
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(AppDataDir)!);
            if (drive.AvailableFreeSpace < 2L * 1024 * 1024 * 1024)
                items.Add(new("aviso", "Sistema", $"Pouco espaço livre no disco {drive.Name} ({drive.AvailableFreeSpace / 1024 / 1024} MB).", "O salvamento automático e as apresentações precisam de espaço."));
        }
        catch { }
    }

    /// <summary>Aponta de novo um arquivo que sumiu; todos os clips que usavam o caminho antigo passam a usar o novo.</summary>
    void RelinkOne(string? oldPath)
    {
        if (string.IsNullOrEmpty(oldPath)) return;
        using var dlg = new OpenFileDialog
        {
            Title = $"Onde está {Path.GetFileName(oldPath)}?",
            FileName = Path.GetFileName(oldPath),
            Filter = $"{Path.GetFileName(oldPath)}|{Path.GetFileName(oldPath)}|Mesmo tipo|*{Path.GetExtension(oldPath)}|Todos os arquivos|*.*",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int n = ReplacePath(oldPath, dlg.FileName);
        // os outros arquivos que sumiram da mesma pasta costumam estar juntos na pasta nova
        int more = SearchMissingIn(Path.GetDirectoryName(dlg.FileName)!, deep: false);
        Send(new { type = "notice", message = $"Arquivo encontrado ({n} clip(s)){(more > 0 ? $" e mais {more} na mesma pasta" : "")}." });
        CompositionChanged();
        _ = RunCheckAsync();
    }

    void RelinkFromFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Pasta onde estão os arquivos (procura também nas subpastas)" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        int n = SearchMissingIn(dlg.SelectedPath, deep: true);
        Send(new { type = "notice", message = n > 0 ? $"{n} arquivo(s) encontrados." : "Nenhum dos arquivos que sumiram está nessa pasta." });
        if (n > 0) CompositionChanged();
        _ = RunCheckAsync();
    }

    /// <summary>Procura pelo nome, numa pasta, os arquivos que sumiram. Devolve quantos achou.</summary>
    int SearchMissingIn(string folder, bool deep)
    {
        var missing = AllClips(_comp).Where(c => c is { Kind: ClipKind.Video, Path: { } p } && !File.Exists(p))
            .Select(c => c!.Path!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (missing.Count == 0) return 0;
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = deep, IgnoreInaccessible = true, MaxRecursionDepth = 6 }))
                byName.TryAdd(Path.GetFileName(f), f);
        }
        catch { }
        int found = 0;
        foreach (var old in missing)
            if (byName.TryGetValue(Path.GetFileName(old), out var now)) { ReplacePath(old, now); found++; }
        return found;
    }

    int ReplacePath(string oldPath, string newPath)
    {
        int n = 0;
        foreach (var c in AllClips(_comp).Concat(_comp.Layers.Select(l => l.PlayingClip)))
            if (c?.Path != null && string.Equals(c.Path, oldPath, StringComparison.OrdinalIgnoreCase)) { c.Path = newPath; n++; }
        return n;
    }
}
