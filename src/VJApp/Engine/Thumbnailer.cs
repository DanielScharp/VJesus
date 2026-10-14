using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Sdcb.FFmpeg.Raw;
using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace VJApp.Engine;

/// <summary>Gera a miniatura (PNG em data URL) do primeiro quadro de um vídeo.</summary>
static unsafe class Thumbnailer
{
    const int ThumbWidth = 192, ThumbHeight = 108;
    const int SWS_AREA = 0x20;

    public static string? Create(string path)
    {
        AVFormatContext* fmt = null;
        AVCodecContext* codec = null;
        AVPacket* pkt = null;
        AVFrame* frame = null;
        SwsContext* sws = null;
        try
        {
            if (avformat_open_input(&fmt, path, null, null) < 0) return null;
            if (avformat_find_stream_info(fmt, null) < 0) return null;
            AVCodec* decoder = null;
            int stream = av_find_best_stream(fmt, AVMediaType.Video, -1, -1, &decoder, 0);
            if (stream < 0) return null;
            codec = avcodec_alloc_context3(decoder);
            avcodec_parameters_to_context(codec, fmt->streams[stream]->codecpar);
            if (avcodec_open2(codec, decoder, null) < 0) return null;

            pkt = av_packet_alloc();
            frame = av_frame_alloc();
            bool got = false;
            for (int reads = 0; !got && reads < 500 && av_read_frame(fmt, pkt) >= 0; reads++)
            {
                if (pkt->stream_index == stream && avcodec_send_packet(codec, pkt) >= 0)
                    got = avcodec_receive_frame(codec, frame) >= 0;
                av_packet_unref(pkt);
            }
            if (!got) return null;

            // mantém a proporção dentro de 192x108
            double scale = Math.Min((double)ThumbWidth / frame->width, (double)ThumbHeight / frame->height);
            int w = Math.Max(2, (int)(frame->width * scale)), h = Math.Max(2, (int)(frame->height * scale));
            sws = sws_getContext(frame->width, frame->height, (AVPixelFormat)frame->format, w, h,
                AVPixelFormat.Bgra, SWS_AREA, null, null, null);
            if (sws == null) return null;

            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            byte*[] dst = [(byte*)data.Scan0, null, null, null];
            int[] dstStride = [data.Stride, 0, 0, 0];
            byte*[] src = [(byte*)frame->data[0], (byte*)frame->data[1], (byte*)frame->data[2], (byte*)frame->data[3]];
            int[] srcStride = [frame->linesize[0], frame->linesize[1], frame->linesize[2], frame->linesize[3]];
            sws_scale(sws, src, srcStride, 0, frame->height, dst, dstStride);
            bmp.UnlockBits(data);

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
        finally
        {
            if (sws != null) sws_freeContext(sws);
            if (frame != null) av_frame_free(&frame);
            if (pkt != null) av_packet_free(&pkt);
            if (codec != null) avcodec_free_context(&codec);
            if (fmt != null) avformat_close_input(&fmt);
        }
    }
}
