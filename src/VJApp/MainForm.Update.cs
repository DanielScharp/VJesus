using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VJApp;

/// <summary>
/// Atualização dentro do programa: confere a última versão publicada no GitHub, baixa o instalador só
/// quando a pessoa pede e instala só com o clique dela (nunca sozinho: pode haver culto em andamento).
/// </summary>
sealed partial class MainForm
{
    const string UpdateApi = "https://api.github.com/repos/DanielScharp/VJesus/releases/latest";
    const string InstallerAsset = "VJesus-Instalador.exe";
    static readonly HttpClient UpdateHttp = CreateUpdateHttp();

    string _updateStatus = "idle"; // idle, checking, none, available, downloading, ready, error
    string? _updateLatest, _updatePage, _updateNotes, _updateError, _updateAssetUrl, _updateDigest, _updateFile;
    long _updateSize;
    int _updateProgress;

    static HttpClient CreateUpdateHttp()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"VJesus/{CurrentVersion}");
        return http;
    }

    public static string CurrentVersion =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    /// <summary>Só a cópia instalada pelo instalador se atualiza sozinha; a portátil abre a página de download.</summary>
    static bool IsInstalled => AppContext.BaseDirectory.StartsWith(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name), StringComparison.OrdinalIgnoreCase);

    void OnUpdateMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "updateCheck": _ = CheckUpdateAsync(manual: true); break;
            case "updateDownload": _ = DownloadUpdateAsync(); break;
            case "updateInstall": _ = InstallUpdateAsync(); break;
        }
    }

    void SendUpdate() => Send(new
    {
        type = "update",
        current = CurrentVersion,
        status = _updateStatus,
        latest = _updateLatest,
        page = _updatePage,
        notes = _updateNotes,
        error = _updateError,
        progress = _updateProgress,
        canInstall = IsInstalled && _updateAssetUrl != null,
    });

    static bool IsNewer(string latest, string current) =>
        Version.TryParse(latest, out var a) && Version.TryParse(current, out var b) && a > b;

    async Task CheckUpdateAsync(bool manual)
    {
        if (_updateStatus is "downloading" or "ready") return;
        _updateStatus = "checking";
        _updateError = null;
        SendUpdate();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, UpdateApi);
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await UpdateHttp.SendAsync(req, cts.Token);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(cts.Token));
            var root = doc.RootElement;
            _updateLatest = root.GetProperty("tag_name").GetString()?.TrimStart('v');
            _updatePage = root.GetProperty("html_url").GetString();
            _updateNotes = root.TryGetProperty("body", out var body) ? body.GetString() : null;
            _updateAssetUrl = _updateDigest = null;
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != InstallerAsset) continue;
                _updateAssetUrl = asset.GetProperty("browser_download_url").GetString();
                _updateSize = asset.GetProperty("size").GetInt64();
                if (asset.TryGetProperty("digest", out var digest) && digest.ValueKind == JsonValueKind.String) _updateDigest = digest.GetString();
            }
            _updateStatus = _updateLatest != null && IsNewer(_updateLatest, CurrentVersion) ? "available" : "none";
        }
        catch (Exception ex)
        {
            // sem internet é normal na igreja: só avisa se a pessoa pediu para conferir
            _updateStatus = manual ? "error" : "idle";
            _updateError = ex is HttpRequestException or TaskCanceledException ? "Não consegui falar com o GitHub. Confira a internet." : ex.Message;
        }
        SendUpdate();
    }

    async Task DownloadUpdateAsync()
    {
        if (_updateStatus != "available" || _updateAssetUrl == null) return;
        _updateStatus = "downloading";
        _updateProgress = 0;
        SendUpdate();
        var dir = Path.Combine(Path.GetTempPath(), "VJesus-atualizacao");
        var file = Path.Combine(dir, InstallerAsset);
        var part = file + ".part";
        try
        {
            Directory.CreateDirectory(dir);
            using var res = await UpdateHttp.GetAsync(_updateAssetUrl, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            long total = res.Content.Headers.ContentLength ?? _updateSize;
            await using (var input = await res.Content.ReadAsStreamAsync())
            await using (var output = File.Create(part))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != _updateProgress) { _updateProgress = pct; SendUpdate(); }
                }
            }
            // confere se chegou inteiro e igual ao publicado
            if (_updateSize > 0 && new FileInfo(part).Length != _updateSize) throw new InvalidDataException("o download veio incompleto");
            if (_updateDigest?.StartsWith("sha256:") == true)
            {
                await using var check = File.OpenRead(part);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(check));
                if (!hash.Equals(_updateDigest[7..], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("o arquivo baixado não confere com o publicado");
            }
            File.Move(part, file, overwrite: true);
            _updateFile = file;
            _updateStatus = "ready";
        }
        catch (Exception ex)
        {
            try { File.Delete(part); } catch { }
            _updateStatus = "error";
            _updateError = $"Não consegui baixar a atualização: {ex.Message}";
        }
        SendUpdate();
    }

    /// <summary>Salva, fecha o programa, roda o instalador com a barra de progresso e abre a versão nova.</summary>
    async Task InstallUpdateAsync()
    {
        if (_updateStatus != "ready" || _updateFile == null || !File.Exists(_updateFile) || Environment.ProcessPath is not { } exe) return;
        _comp.Save(VJApp.Model.Composition.DefaultPath);
        if (_projectPath != null) await WriteProjectAsync(_projectPath, silent: true);
        Process.Start(new ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c start \"\" /wait \"{_updateFile}\" /passive & start \"\" \"{exe}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        Close();
    }

    /// <summary>Confere uma vez, um pouco depois de abrir, sem atrapalhar a abertura.</summary>
    void StartUpdateCheck()
    {
        var t = new System.Windows.Forms.Timer { Interval = 8000 };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); _ = CheckUpdateAsync(manual: false); };
        t.Start();
    }
}
