using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Prévia: desenha um slide igual ao do telão (fonte, estilo, cena), numa imagem só para o operador. Nada vai ao ar.</summary>
sealed partial class MainForm
{
    async Task RenderPreviewAsync(JsonNode msg)
    {
        string? id = (string?)msg["id"];
        try
        {
            var (text, style) = await PreviewContentAsync(msg);
            string? data = text == null ? null : await Task.Run(() => PreviewImage(LyricsFeed.Render(text, style)));
            Send(new { type = "slidePreview", id, data });
        }
        catch (Exception ex)
        {
            Send(new { type = "slidePreview", id, data = (string?)null, error = ex.Message });
        }
    }

    /// <summary>O texto e o estilo que aquele conteúdo teria no telão.</summary>
    async Task<(string? Text, LyricStyle Style)> PreviewContentAsync(JsonNode msg)
    {
        var o = _service.Overlay;
        switch ((string?)msg["kind"])
        {
            case "song":
            {
                var song = _lyrics.Songs.FirstOrDefault(s => s.Id == (string?)msg["songId"]);
                var slides = song != null ? LyricsLibrary.Slides(song.Text) : [];
                int i = (int?)msg["slide"] ?? 0;
                if (i < 0 || i >= slides.Count) return (null, _lyrics.Style);
                return (slides[i].Text, _lyrics.ResolveStyle(song, slides[i]));
            }
            case "bible":
            {
                int verse = (int)msg["verse"]!;
                var p = new BiblePassage((string)msg["book"]!, (int)msg["chapter"]!, verse, (int?)msg["verseEnd"] ?? verse);
                var s = _bible.Settings;
                await EnsureChapterAsync((s.OnScreen.Count > 0 ? s.OnScreen : []).Append(s.MainVersion).OfType<string>().ToList(), p.Book, p.Chapter);
                return (ComposeBible(p), BibleStyle());
            }
            case "notice": return ((string?)msg["text"], o.NoticeStyle);
            case "timer": return (TimerText(o.TimerLabel, "", (double?)msg["seconds"] ?? 0), o.TimerStyle);
            default: return ((string?)msg["text"], _lyrics.Style);
        }
    }

    /// <summary>Junta as camadas do slide sobre preto, em tamanho de monitor do operador.</summary>
    static string PreviewImage(LyricsFrame frame)
    {
        using var image = new Bitmap(640, 360, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            foreach (var layer in new[] { frame.Box, frame.Static, frame.Animated })
            {
                if (layer == null) continue;
                var pin = GCHandle.Alloc(layer, GCHandleType.Pinned);
                try
                {
                    using var bmp = new Bitmap(LyricsFeed.Width, LyricsFeed.Height, LyricsFeed.Width * 4, PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
                    g.DrawImage(bmp, 0, 0, image.Width, image.Height);
                }
                finally { pin.Free(); }
            }
        }
        using var ms = new MemoryStream();
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var pars = new EncoderParameters(1);
        pars.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
        image.Save(ms, jpeg, pars);
        return "data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray());
    }
}
