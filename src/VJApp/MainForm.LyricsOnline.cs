using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Busca de letras na internet pelo LRCLIB (lrclib.net): base aberta e gratuita de letras, com API pública
/// sem chave. A pessoa escolhe o resultado e a letra entra na biblioteca já dividida em slides.
/// </summary>
sealed partial class MainForm
{
    static readonly HttpClient LyricsHttp = CreateLyricsHttp();

    static HttpClient CreateLyricsHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // o LRCLIB pede que cada programa se identifique
        h.DefaultRequestHeaders.UserAgent.ParseAdd("VJesus/1.13 (https://github.com/DanielScharp/VJesus)");
        return h;
    }

    async Task SearchLyricsOnlineAsync(string query)
    {
        query = query.Trim();
        if (query.Length < 2) return;
        try
        {
            var json = await LyricsHttp.GetStringAsync($"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}");
            using var doc = JsonDocument.Parse(json);
            var seen = new HashSet<string>();
            var list = new List<object>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                string S(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var text = S("plainLyrics").Trim();
                if (text.Length == 0) continue; // instrumental ou sem letra
                text = CleanOnlineLyrics(text);
                // a mesma letra aparece em várias gravações (ao vivo, acústico…): mostra uma vez só
                var key = Regex.Replace(Lyrics.Fold(text), @"\W+", "");
                if (!seen.Add(key[..Math.Min(key.Length, 400)])) continue;
                list.Add(new
                {
                    id = r.TryGetProperty("id", out var id) ? id.ToString() : "",
                    title = S("trackName"),
                    artist = S("artistName"),
                    album = S("albumName"),
                    duration = r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0,
                    text,
                });
                if (list.Count >= 25) break;
            }
            Send(new { type = "lyricsWebResults", query, results = list });
        }
        catch (Exception ex)
        {
            Send(new { type = "lyricsWebResults", query, results = Array.Empty<object>(), error = $"Não consegui buscar na internet ({ex.Message}). Confira a conexão." });
        }
    }

    /// <summary>Letra pronta para slides: estrofes separadas por linha em branco, sem espaços sobrando.</summary>
    static string CleanOnlineLyrics(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim());
        var clean = string.Join("\n", lines);
        clean = Regex.Replace(clean, @"\n{3,}", "\n\n");
        return clean.Trim();
    }
}
