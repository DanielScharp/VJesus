using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// "Ver" um clip só no monitor do operador, sem mandar para os telões: a interface toca o arquivo direto
/// (vídeo ou imagem) por um endereço local https://media.vj/?p=caminho, com suporte a avançar/voltar no vídeo.
/// </summary>
sealed partial class MainForm
{
    const string MediaHost = "media.vj";

    void SetupMediaPreviewServer(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter($"https://{MediaHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            if (!e.Request.Uri.StartsWith($"https://{MediaHost}/", StringComparison.OrdinalIgnoreCase)) return;
            try { e.Response = ServeMedia(core.Environment, e.Request); }
            catch { e.Response = core.Environment.CreateWebResourceResponse(null, 500, "Erro", ""); }
        };
    }

    static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".mov"] = "video/quicktime", [".webm"] = "video/webm", [".mkv"] = "video/x-matroska",
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".bmp"] = "image/bmp", [".gif"] = "image/gif", [".webp"] = "image/webp",
    };

    /// <summary>Entrega o arquivo pedido (só vídeo/imagem), em partes quando o player pede um trecho (para poder pular no vídeo).</summary>
    static CoreWebView2WebResourceResponse ServeMedia(CoreWebView2Environment env, CoreWebView2WebResourceRequest req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(req.Uri).Query);
        string? path = query["p"];
        string ext = Path.GetExtension(path ?? "").ToLowerInvariant();
        if (path == null || !File.Exists(path) || !(MediaTypes.ContainsKey(ext) || VideoExtensions.Contains(ext) || ImageExtensions.Contains(ext)))
            return env.CreateWebResourceResponse(null, 404, "Não encontrado", "");
        string type = MediaTypes.TryGetValue(ext, out var t) ? t : "application/octet-stream";
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = fs.Length;
        string? range = req.Headers.Contains("Range") ? req.Headers.GetHeader("Range") : null;
        if (range != null && range.StartsWith("bytes="))
        {
            var parts = range[6..].Split('-');
            long start = long.TryParse(parts[0], out var s) ? s : 0;
            long end = parts.Length > 1 && long.TryParse(parts[1], out var en) ? en : Math.Min(length - 1, start + 8 * 1024 * 1024 - 1);
            end = Math.Min(end, length - 1);
            if (start >= length) { fs.Dispose(); return env.CreateWebResourceResponse(null, 416, "Fora do arquivo", $"Content-Range: bytes */{length}"); }
            fs.Position = start;
            var slice = new byte[end - start + 1];
            int read = 0;
            while (read < slice.Length) { int n = fs.Read(slice, read, slice.Length - read); if (n <= 0) break; read += n; }
            fs.Dispose();
            return env.CreateWebResourceResponse(new MemoryStream(slice, 0, read), 206, "Partial Content",
                $"Content-Type: {type}\r\nAccept-Ranges: bytes\r\nContent-Range: bytes {start}-{start + read - 1}/{length}\r\nContent-Length: {read}\r\nCache-Control: no-store");
        }
        return env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {type}\r\nAccept-Ranges: bytes\r\nContent-Length: {length}\r\nCache-Control: no-store");
    }

    /// <summary>O que mostrar no "Ver" de uma célula: arquivo de vídeo/imagem ou slides de uma apresentação.</summary>
    void SendClipPreviewInfo(JsonNode msg)
    {
        int layer = (int?)msg["layer"] ?? -1, column = (int?)msg["column"] ?? -1;
        string? path = (string?)msg["path"];
        Clip? clip = null;
        if (path == null && layer >= 0 && layer < _comp.Layers.Count && column >= 0 && column < _comp.Columns)
        {
            clip = _comp.Layers[layer].Clips[column];
            path = clip?.Path;
        }
        string kind = "none";
        string[] slides = [];
        if (clip?.Kind == ClipKind.Presentation || (path != null && Directory.Exists(path)))
        {
            slides = [.. PresentationSlides(path)];
            kind = slides.Length > 0 ? "slides" : "none";
        }
        else if (path != null && File.Exists(path))
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            kind = ImageExtensions.Contains(ext) ? "image" : VideoExtensions.Contains(ext) ? "video" : "none";
        }
        Send(new
        {
            type = "clipPreviewInfo",
            id = (string?)msg["id"],
            kind,
            name = clip?.Name ?? (path != null ? Path.GetFileNameWithoutExtension(path) : ""),
            clipKind = clip?.Kind.ToString(),
            url = kind is "video" or "image" ? $"https://{MediaHost}/?p={Uri.EscapeDataString(path!)}" : null,
            slides = slides.Select(s => $"https://{MediaHost}/?p={Uri.EscapeDataString(s)}"),
            path,
        });
    }
}
