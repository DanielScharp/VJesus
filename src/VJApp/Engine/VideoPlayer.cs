using System.Diagnostics;
using Sdcb.FFmpeg.Raw;
using Vortice.DXGI;
using static Sdcb.FFmpeg.Raw.ffmpeg;

namespace VJApp.Engine;

/// <summary>
/// Quadro pronto para subir na GPU: BGRA (vídeo comum) ou blocos DXT/BC (HAP, sem descompactar na CPU).
/// </summary>
sealed class VideoFrame(int bytes, int stride, int rows)
{
    public readonly byte[] Pixels = new byte[bytes];
    /// <summary>Bytes por linha (de pixels ou de blocos 4×4).</summary>
    public readonly int Stride = stride;
    /// <summary>Linhas (de pixels ou de blocos 4×4).</summary>
    public readonly int Rows = rows;
    /// <summary>Tempo de apresentação contínuo (cresce a cada volta do loop).</summary>
    public double Time;
}

/// <summary>
/// Lê um arquivo de vídeo (ou câmera) com FFmpeg numa thread própria e entrega quadros para o render
/// thread. HAP vai direto para a GPU em blocos DXT; os demais codecs são decodificados para BGRA.
/// Se o arquivo tiver áudio, ele é convertido para float estéreo 48 kHz e fica num buffer para o mixer.
/// Toca em loop; suporta pausa e seek.
/// </summary>
sealed unsafe class VideoPlayer : IDisposable
{
    const int QueueSize = 4;
    const int AVERROR_EOF = -541478725;
    const int SWS_BILINEAR = 2;
    const int AVSEEK_FLAG_BACKWARD = 1;
    const long NoPts = long.MinValue; // AV_NOPTS_VALUE

    public const int AudioRate = 48000;

    public string FilePath { get; }
    public string CodecName { get; }
    public int Width { get; }
    public int Height { get; }
    public double Duration { get; }
    public double FrameRate { get; }
    /// <summary>Câmera/dispositivo ao vivo (sem duração; sempre mostra o quadro mais novo).</summary>
    public bool IsLive { get; }
    /// <summary>Formato da textura: B8G8R8A8 (BGRA) ou BC1/BC3/BC7 (HAP direto na GPU).</summary>
    public Format TextureFormat { get; } = Format.B8G8R8A8_UNorm;
    public HapColor Color { get; } = HapColor.Rgb;
    /// <summary>Tamanho da textura (HAP arredonda para múltiplos de 4).</summary>
    public int TextureWidth { get; }
    public int TextureHeight { get; }
    public bool HasAudio => _audioCodec != null;

    AVFormatContext* _fmt;
    AVCodecContext* _codec;
    SwsContext* _sws;
    readonly int _stream;
    readonly AVRational _timeBase;
    readonly long _startPts;
    readonly bool _hap;

    // áudio
    readonly int _audioStream = -1;
    AVCodecContext* _audioCodec;
    SwrContext* _swr;
    readonly float[] _audioRing = new float[AudioRate * 2 * 2]; // 2 s estéreo
    int _audioRead, _audioCount;

    readonly Thread _thread;
    volatile bool _running = true;

    // fila de quadros prontos e quadros livres
    readonly object _lock = new();
    readonly Queue<VideoFrame> _ready = new();
    readonly Stack<VideoFrame> _free = new();

    // relógio de reprodução (tempo contínuo; posição = tempo % duração)
    readonly Stopwatch _clock = new();
    double _clockBase;
    double _seekTarget = -1;
    int _loops;

    public bool Playing => _clock.IsRunning;

    static bool _devicesRegistered;

    static void RegisterDevices()
    {
        if (_devicesRegistered) return;
        avdevice_register_all();
        _devicesRegistered = true;
    }

    /// <summary>Câmeras e entradas de vídeo do Windows (DirectShow): webcams, placas de captura, Câmera Virtual do OBS.</summary>
    public static List<string> ListCameras()
    {
        RegisterDevices();
        var names = new List<string>();
        AVDeviceInfoList* list = null;
        if (avdevice_list_input_sources(av_find_input_format("dshow"), null, null, &list) >= 0 && list != null)
        {
            for (int i = 0; i < list->nb_devices; i++)
            {
                var dev = list->devices[i];
                bool video = false;
                for (int k = 0; k < dev->nb_media_types; k++) video |= dev->media_types[k] == AVMediaType.Video;
                if (video) names.Add(new string((sbyte*)dev->device_description));
            }
            avdevice_free_list_devices(&list);
        }
        return names;
    }

    /// <param name="path">Arquivo de vídeo, ou nome do dispositivo quando <paramref name="liveDevice"/> é true.</param>
    public VideoPlayer(string path, bool liveDevice = false)
    {
        FilePath = path;
        IsLive = liveDevice;
        AVFormatContext* fmt = null;
        if (liveDevice)
        {
            RegisterDevices();
            AVDictionary* opts = null;
            av_dict_set(&opts, "rtbufsize", "128M", 0);
            int r = avformat_open_input(&fmt, "video=" + path, av_find_input_format("dshow"), &opts);
            av_dict_free(&opts);
            Check(r, $"abrir a câmera {path}");
        }
        else
        {
            Check(avformat_open_input(&fmt, path, null, null), "abrir arquivo");
        }
        _fmt = fmt;
        Check(avformat_find_stream_info(_fmt, null), "ler streams");

        AVCodec* decoder = null;
        _stream = av_find_best_stream(_fmt, AVMediaType.Video, -1, -1, &decoder, 0);
        Check(_stream, "achar stream de vídeo");

        var st = _fmt->streams[_stream];
        _timeBase = st->time_base;
        _startPts = st->start_time == NoPts ? 0 : st->start_time;
        CodecName = new string((sbyte*)decoder->name);
        Width = st->codecpar->width;
        Height = st->codecpar->height;
        Duration = IsLive ? 0 : _fmt->duration > 0 ? _fmt->duration / 1_000_000.0 : st->duration * Q(_timeBase);
        FrameRate = st->avg_frame_rate.Den != 0 ? Q(st->avg_frame_rate) : 30;

        // HAP: lê o primeiro pacote para descobrir a variante; se suportada, pula o decoder do FFmpeg
        HapFrame.Info? hap = st->codecpar->codec_id == AVCodecID.Hap ? ProbeHap() : null;
        int framesBytes, stride, rows;
        if (hap != null)
        {
            _hap = true;
            TextureFormat = hap.Format;
            Color = hap.Color;
            TextureWidth = (Width + 3) / 4 * 4;
            TextureHeight = (Height + 3) / 4 * 4;
            stride = TextureWidth / 4 * hap.BlockBytes;
            rows = TextureHeight / 4;
            framesBytes = stride * rows;
            CodecName = "hap (GPU)";
        }
        else
        {
            _codec = avcodec_alloc_context3(decoder);
            Check(avcodec_parameters_to_context(_codec, st->codecpar), "configurar codec");
            _codec->thread_count = 0; // automático
            Check(avcodec_open2(_codec, decoder, null), "abrir codec");
            _sws = sws_getContext(Width, Height, _codec->pix_fmt, Width, Height, AVPixelFormat.Bgra,
                SWS_BILINEAR, null, null, null);
            if (_sws == null) throw new InvalidOperationException("Formato de pixel não suportado.");
            TextureWidth = Width;
            TextureHeight = Height;
            stride = Width * 4;
            rows = Height;
            framesBytes = stride * rows;
        }
        for (int i = 0; i < QueueSize; i++) _free.Push(new VideoFrame(framesBytes, stride, rows));

        if (!IsLive) _audioStream = OpenAudio();

        _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "Decode " + Path.GetFileName(path) };
        _thread.Start();
        _clock.Start();
    }

    HapFrame.Info? ProbeHap()
    {
        AVPacket* pkt = av_packet_alloc();
        try
        {
            for (int i = 0; i < 50 && av_read_frame(_fmt, pkt) >= 0; i++)
            {
                if (pkt->stream_index == _stream)
                    return HapFrame.Probe(new ReadOnlySpan<byte>(pkt->data, pkt->size));
                av_packet_unref(pkt);
            }
            return null;
        }
        finally
        {
            av_packet_free(&pkt);
            av_seek_frame(_fmt, _stream, 0, AVSEEK_FLAG_BACKWARD);
        }
    }

    int OpenAudio()
    {
        AVCodec* dec = null;
        int index = av_find_best_stream(_fmt, AVMediaType.Audio, -1, _stream, &dec, 0);
        if (index < 0 || dec == null) return -1;
        try
        {
            _audioCodec = avcodec_alloc_context3(dec);
            Check(avcodec_parameters_to_context(_audioCodec, _fmt->streams[index]->codecpar), "configurar áudio");
            Check(avcodec_open2(_audioCodec, dec, null), "abrir áudio");

            AVChannelLayout outLayout;
            av_channel_layout_default(&outLayout, 2);
            SwrContext* swr = null;
            Check(swr_alloc_set_opts2(&swr, &outLayout, AVSampleFormat.Flt, AudioRate,
                &_audioCodec->ch_layout, _audioCodec->sample_fmt, _audioCodec->sample_rate, 0, null), "configurar conversão de áudio");
            Check(swr_init(swr), "iniciar conversão de áudio");
            _swr = swr;
            return index;
        }
        catch
        {
            // sem áudio não é erro: o vídeo toca mudo
            if (_audioCodec != null) { var c = _audioCodec; avcodec_free_context(&c); _audioCodec = null; }
            return -1;
        }
    }

    static double Q(AVRational r) => r.Den == 0 ? 0 : (double)r.Num / r.Den;

    static void Check(int ret, string what)
    {
        if (ret >= 0) return;
        byte* buf = stackalloc byte[256];
        av_strerror(ret, buf, 256);
        throw new InvalidOperationException($"FFmpeg falhou ao {what}: {new string((sbyte*)buf)}");
    }

    // ---------- controle (chamado pela UI) ----------

    public double Position
    {
        get
        {
            lock (_lock)
            {
                double t = _clockBase + _clock.Elapsed.TotalSeconds;
                return Duration > 0 ? t % Duration : t;
            }
        }
    }

    public void Play() { lock (_lock) _clock.Start(); }
    public void Pause() { lock (_lock) _clock.Stop(); }

    public void Seek(double seconds)
    {
        lock (_lock)
        {
            seconds = Math.Clamp(seconds, 0, Math.Max(0, Duration - 0.001));
            _seekTarget = seconds;
            _clockBase = seconds;
            if (_clock.IsRunning) _clock.Restart(); else _clock.Reset();
            while (_ready.Count > 0) _free.Push(_ready.Dequeue());
            _audioCount = 0;
            Monitor.PulseAll(_lock);
        }
    }

    // ---------- consumo (render thread e mixer de áudio) ----------

    /// <summary>Retorna o quadro que deve estar na tela agora, ou null se nada mudou.</summary>
    public VideoFrame? TakeFrame(VideoFrame? previous)
    {
        lock (_lock)
        {
            double now = _clockBase + _clock.Elapsed.TotalSeconds;
            VideoFrame? pick = null;
            while (_ready.Count > 0 && (_ready.Peek().Time <= now || pick == null && previous == null))
            {
                if (pick != null) _free.Push(pick);
                pick = _ready.Dequeue();
            }
            if (pick == null) return null;
            if (previous != null) _free.Push(previous);
            Monitor.PulseAll(_lock);
            return pick;
        }
    }

    /// <summary>Soma (com ganho) o áudio disponível em <paramref name="dest"/> (float estéreo intercalado).</summary>
    public void MixAudio(Span<float> dest, float gain)
    {
        if (!HasAudio || !Playing) return;
        lock (_lock)
        {
            int n = Math.Min(dest.Length, _audioCount);
            for (int i = 0; i < n; i++)
            {
                dest[i] += _audioRing[_audioRead] * gain;
                _audioRead = (_audioRead + 1) % _audioRing.Length;
            }
            _audioCount -= n;
        }
    }

    // ---------- decodificação ----------

    void DecodeLoop()
    {
        AVPacket* pkt = av_packet_alloc();
        AVFrame* frame = av_frame_alloc();
        try
        {
            double dropBefore = -1;
            while (_running)
            {
                // seek pendente?
                double target;
                lock (_lock) { target = _seekTarget; _seekTarget = -1; }
                if (target >= 0)
                {
                    long ts = (long)(target / Q(_timeBase));
                    av_seek_frame(_fmt, _stream, ts, AVSEEK_FLAG_BACKWARD);
                    if (_codec != null) avcodec_flush_buffers(_codec);
                    if (_audioCodec != null) avcodec_flush_buffers(_audioCodec);
                    lock (_lock) { _loops = 0; _audioCount = 0; }
                    dropBefore = target - 0.5 / FrameRate;
                }

                int ret = av_read_frame(_fmt, pkt);
                if (ret < 0 && IsLive) { Thread.Sleep(5); continue; } // câmera sem quadro novo ainda
                if (ret == AVERROR_EOF)
                {
                    // fim do arquivo: drena o decoder e volta para o início (loop)
                    if (_codec != null)
                    {
                        avcodec_send_packet(_codec, null);
                        while (avcodec_receive_frame(_codec, frame) >= 0) Deliver(frame, ref dropBefore);
                        avcodec_flush_buffers(_codec);
                    }
                    if (_audioCodec != null) avcodec_flush_buffers(_audioCodec);
                    av_seek_frame(_fmt, _stream, 0, AVSEEK_FLAG_BACKWARD);
                    lock (_lock) _loops++;
                    continue;
                }
                if (ret < 0) { Thread.Sleep(5); continue; }

                if (pkt->stream_index == _stream)
                {
                    if (_hap)
                    {
                        DeliverHap(pkt, ref dropBefore);
                    }
                    else if (avcodec_send_packet(_codec, pkt) >= 0)
                    {
                        while (_running && avcodec_receive_frame(_codec, frame) >= 0)
                            Deliver(frame, ref dropBefore);
                    }
                }
                else if (pkt->stream_index == _audioStream && avcodec_send_packet(_audioCodec, pkt) >= 0)
                {
                    while (avcodec_receive_frame(_audioCodec, frame) >= 0)
                        WriteAudio(frame, dropBefore);
                }
                av_packet_unref(pkt);
            }
        }
        finally
        {
            av_frame_free(&frame);
            av_packet_free(&pkt);
        }
    }

    /// <summary>Pega um quadro livre para escrever (ou null se o player está parando/buscando).</summary>
    VideoFrame? AcquireSlot(double t)
    {
        lock (_lock)
        {
            if (IsLive)
            {
                // ao vivo: nunca espera; se a fila encheu, reaproveita o quadro mais antigo (menos atraso)
                var s = _free.Count > 0 ? _free.Pop() : _ready.Count > 0 ? _ready.Dequeue() : null;
                if (s != null) s.Time = _clockBase + _clock.Elapsed.TotalSeconds;
                return s;
            }
            while (_running && _free.Count == 0 && _seekTarget < 0) Monitor.Wait(_lock, 50);
            if (!_running || _seekTarget >= 0) return null;
            var slot = _free.Pop();
            slot.Time = t + _loops * Duration;
            return slot;
        }
    }

    void Publish(VideoFrame slot)
    {
        lock (_lock)
        {
            if (_seekTarget >= 0) { _free.Push(slot); return; }
            _ready.Enqueue(slot);
        }
    }

    double PtsToSeconds(long pts) => pts == NoPts ? 0 : (pts - _startPts) * Q(_timeBase);

    void Deliver(AVFrame* frame, ref double dropBefore)
    {
        double t = PtsToSeconds(frame->best_effort_timestamp);
        if (t < dropBefore) return; // quadros antes do ponto de seek
        dropBefore = -1;

        var slot = AcquireSlot(t);
        if (slot == null) return;
        fixed (byte* dst = slot.Pixels)
        {
            byte*[] dstData = [dst, null, null, null];
            int[] dstStride = [slot.Stride, 0, 0, 0];
            byte*[] srcData = [(byte*)frame->data[0], (byte*)frame->data[1], (byte*)frame->data[2], (byte*)frame->data[3]];
            int[] srcStride = [frame->linesize[0], frame->linesize[1], frame->linesize[2], frame->linesize[3]];
            sws_scale(_sws, srcData, srcStride, 0, Height, dstData, dstStride);
        }
        Publish(slot);
    }

    /// <summary>HAP: só tira a compressão Snappy; os blocos DXT vão como estão para a GPU.</summary>
    void DeliverHap(AVPacket* pkt, ref double dropBefore)
    {
        double t = PtsToSeconds(pkt->pts != NoPts ? pkt->pts : pkt->dts);
        if (t < dropBefore) return;
        dropBefore = -1;

        var slot = AcquireSlot(t);
        if (slot == null) return;
        if (!HapFrame.Decode(new ReadOnlySpan<byte>(pkt->data, pkt->size), slot.Pixels))
        {
            lock (_lock) _free.Push(slot); // pacote corrompido: pula o quadro
            return;
        }
        Publish(slot);
    }

    void WriteAudio(AVFrame* frame, double dropBefore)
    {
        if (_swr == null) return;
        if (dropBefore >= 0 && PtsToSecondsAudio(frame->best_effort_timestamp) < dropBefore) return;

        int maxOut = (int)av_rescale_rnd(swr_get_delay(_swr, _audioCodec->sample_rate) + frame->nb_samples,
            AudioRate, _audioCodec->sample_rate, AVRounding.Up);
        var tmp = new float[maxOut * 2];
        int got;
        fixed (float* outPtr = tmp)
        {
            byte* outBuf = (byte*)outPtr;
            got = swr_convert(_swr, &outBuf, maxOut, frame->extended_data, frame->nb_samples);
        }
        if (got <= 0) return;

        lock (_lock)
        {
            int n = got * 2;
            for (int i = 0; i < n; i++)
            {
                if (_audioCount == _audioRing.Length) { _audioRead = (_audioRead + 1) % _audioRing.Length; _audioCount--; } // cheio: perde o mais antigo
                _audioRing[(_audioRead + _audioCount) % _audioRing.Length] = tmp[i];
                _audioCount++;
            }
        }
    }

    double PtsToSecondsAudio(long pts)
    {
        if (pts == NoPts || _audioStream < 0) return 0;
        var st = _fmt->streams[_audioStream];
        long start = st->start_time == NoPts ? 0 : st->start_time;
        return (pts - start) * Q(st->time_base);
    }

    public void Dispose()
    {
        _running = false;
        lock (_lock) Monitor.PulseAll(_lock);
        _thread.Join(2000);
        if (_sws != null) { sws_freeContext(_sws); _sws = null; }
        if (_swr != null) { var s = _swr; swr_free(&s); _swr = null; }
        if (_codec != null) { var c = _codec; avcodec_free_context(&c); _codec = null; }
        if (_audioCodec != null) { var a = _audioCodec; avcodec_free_context(&a); _audioCodec = null; }
        var f = _fmt; avformat_close_input(&f); _fmt = null;
    }
}
