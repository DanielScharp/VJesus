using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Model;

namespace VJApp;

/// <summary>
/// Animação de letra (tipografia cinética) ligada à música: quando a música tem a animação ligada,
/// cada slide que vai ao ar entra animado (efeito, fonte e cores escolhidos para aquele slide) na
/// layer do clip Letras. A página escondida (KineticWindow) desenha ao vivo; passar o slide troca a animação.
/// </summary>
sealed partial class MainForm
{
    KineticWindow? _kineticWindow;
    bool _kineticOn;
    int _kineticSeq;

    /// <summary>A animação desta música, se estiver ligada.</summary>
    static JsonNode? KineticOf(Song? song) => song?.Kinetic is { } k && (bool?)k["enabled"] == true ? k : null;

    /// <summary>Chamado sempre que o que está no telão muda (slide, branco, limpar, outra música, Bíblia).</summary>
    void UpdateKinetic()
    {
        var song = _liveSongId != null && _liveBible == null ? _lyrics.Songs.FirstOrDefault(s => s.Id == _liveSongId) : null;
        var kin = KineticOf(song);
        string? text = _blank ? null : _liveText;
        if (kin != null)
        {
            var styles = kin["styles"] as JsonArray;
            var style = styles != null && _liveSlide >= 0 && _liveSlide < styles.Count ? styles[_liveSlide] : null;
            PostKineticScene(new
            {
                type = "kineticScene",
                seq = ++_kineticSeq,
                text,
                index = _liveSlide,
                style,
                transparent = (bool?)kin["transparent"] == true,
                light = (bool?)kin["light"] == true,
            });
            if (!_kineticOn)
            {
                _kineticOn = true;
                _lyricsFeed.Kinetic = _kineticWindow!.Source;
            }
            return;
        }
        if (!_kineticOn) return;
        if (text == null)
        {
            // fim da música ou tela em branco: a animação sai e fica vazio (continua ligada para a saída aparecer)
            PostKineticScene(new { type = "kineticScene", seq = ++_kineticSeq, text = (string?)null, index = -1, style = (JsonNode?)null, transparent = true, light = false });
            return;
        }
        // outra coisa no ar (música sem animação, versículo, texto rápido): volta para a letra normal
        _kineticOn = false;
        _lyricsFeed.Kinetic = null;
        PostKineticScene(new { type = "kineticScene", seq = ++_kineticSeq, text = (string?)null, index = -1, style = (JsonNode?)null, transparent = true, light = false, cut = true });
    }

    /// <summary>
    /// Abre a página da animação antes da hora (ela leva um instante para carregar): assim o primeiro slide
    /// animado já entra no tempo certo. Só quando alguma música usa a animação.
    /// </summary>
    void WarmUpKinetic()
    {
        if (_kineticWindow != null || !_lyrics.Songs.Any(s => KineticOf(s) != null)) return;
        _kineticWindow = new KineticWindow(SendKineticFonts);
    }

    void PostKineticScene(object scene)
    {
        _kineticWindow ??= new KineticWindow(SendKineticFonts);
        _kineticWindow.Post(JsonSerializer.Serialize(scene, Json), isScene: true);
    }

    void SendKineticFonts()
    {
        if (_kineticWindow == null) return;
        var payload = new
        {
            type = "fonts",
            list = Array.Empty<string>(),
            faces = Engine.FontStore.Files.Select(f => new
            {
                family = f.Family,
                url = "https://fonts.vj/" + string.Join("/", Path.GetRelativePath(Engine.FontStore.Dir, f.File).Split(Path.DirectorySeparatorChar).Select(Uri.EscapeDataString)),
                bold = f.Bold,
                italic = f.Italic,
            }),
        };
        _kineticWindow.Post(JsonSerializer.Serialize(payload, Json));
    }
}
