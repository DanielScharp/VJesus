using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace VJApp.Engine;

/// <summary>O que a UI pede para uma tela de saída: a janela e os triângulos de slices e máscaras.</summary>
sealed record OutputSpec(IntPtr Hwnd, int Width, int Height, List<(int Source, float[] Vertices)> SliceBatches, float[] MaskVertices);

/// <summary>Uma tela de saída do lado do motor (só usada pelo render thread).</summary>
sealed class OutputTarget(IntPtr hwnd) : IDisposable
{
    public readonly IntPtr Hwnd = hwnd;
    public IDXGISwapChain1? SwapChain;
    public ID3D11RenderTargetView? Rtv;
    public int Width, Height;
    /// <summary>Slices agrupados pela fonte (-1 = composição inteira; n = grupo de layers n).</summary>
    public readonly List<(int Source, ID3D11Buffer Vb, int Count)> SliceBatches = [];
    public ID3D11Buffer? MaskVb;
    public int MaskCount;

    public void ClearBatches()
    {
        foreach (var b in SliceBatches) b.Vb.Dispose();
        SliceBatches.Clear();
    }

    public void Dispose()
    {
        ClearBatches(); MaskVb?.Dispose();
        Rtv?.Dispose(); SwapChain?.Dispose();
    }
}

partial class Renderer
{
    public const int PreviewWidth = 960, PreviewHeight = 540;

    /// <summary>Preview reduzido da composição (BGRA, largura, altura, bytes por linha).</summary>
    public event Action<byte[], int, int, int>? Preview;

    volatile IReadOnlyList<OutputSpec>? _pendingOutputs;
    readonly Dictionary<IntPtr, (int W, int H)> _pendingResizes = [];
    readonly List<OutputTarget> _outputs = [];
    IDXGIFactory2? _factory;
    ID3D11VertexShader? _vsSlice;
    ID3D11PixelShader? _psSlice, _psMask, _psBlit;
    ID3D11InputLayout? _sliceLayout;
    PreviewChannel? _mainPreview, _stagePreview;
    readonly RenderTexture?[] _stageComp = new RenderTexture?[2];

    /// <summary>Imagem que vai para o item "Telão" da tela de retorno (só quando há algo para mostrar).</summary>
    public event Action<byte[], int, int, int>? StagePreview;
    /// <summary>Retorno precisa da imagem? (tela de retorno ligada com o item "Telão" visível)</summary>
    public volatile bool StageFeedWanted;
    /// <summary>Espelhar no retorno tudo o que está no telão.</summary>
    public volatile bool StageMirror;

    /// <summary>Cópia reduzida de uma composição para a CPU, ~15 vezes por segundo, sem travar a GPU.</summary>
    sealed class PreviewChannel(ID3D11Device device) : IDisposable
    {
        public readonly RenderTexture Rt = new(device, PreviewWidth, PreviewHeight);
        public readonly ID3D11Texture2D Staging = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, PreviewWidth, PreviewHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
        public bool Pending;
        public long Last;
        public void Dispose() { Staging.Dispose(); Rt.Dispose(); }
    }

    /// <summary>Define as telas de saída e o mapeamento de cada uma (substitui o conjunto anterior).</summary>
    public void SetOutputs(IReadOnlyList<OutputSpec> outputs) => _pendingOutputs = outputs;

    public void ResizeOutput(IntPtr hwnd, int width, int height)
    {
        lock (_pendingResizes) _pendingResizes[hwnd] = (Math.Max(1, width), Math.Max(1, height));
    }

    void InitOutputs(byte[] sliceVsCode, string src, string path)
    {
        using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        _factory = adapter.GetParent<IDXGIFactory2>();

        _vsSlice = _device.CreateVertexShader(sliceVsCode);
        _psSlice = _device.CreatePixelShader(Vortice.D3DCompiler.Compiler.Compile(src, "PSSlice", path, "ps_5_0").Span);
        _psMask = _device.CreatePixelShader(Vortice.D3DCompiler.Compiler.Compile(src, "PSMask", path, "ps_5_0").Span);
        _psBlit = _device.CreatePixelShader(Vortice.D3DCompiler.Compiler.Compile(src, "PSBlit", path, "ps_5_0").Span);
        _sliceLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
            new InputElementDescription("TEXCOORD", 1, Format.R32G32_Float, 16, 0),
            new InputElementDescription("TEXCOORD", 2, Format.R32G32B32A32_Float, 24, 0),
            new InputElementDescription("TEXCOORD", 3, Format.R32G32_Float, 40, 0),
        ], sliceVsCode);

        _mainPreview = new PreviewChannel(_device);
        _stagePreview = new PreviewChannel(_device);
    }

    /// <summary>Cria/remove swap chains conforme as telas pedidas e atualiza os vértices.</summary>
    void ApplyPendingOutputs()
    {
        var specs = Interlocked.Exchange(ref _pendingOutputs, null);
        if (specs != null)
        {
            foreach (var gone in _outputs.Where(o => specs.All(s => s.Hwnd != o.Hwnd)).ToList())
            {
                _ctx!.ClearState();
                gone.Dispose();
                _outputs.Remove(gone);
            }
            foreach (var spec in specs)
            {
                var o = _outputs.FirstOrDefault(x => x.Hwnd == spec.Hwnd);
                if (o == null)
                {
                    o = new OutputTarget(spec.Hwnd);
                    CreateSwapChain(o, spec.Width, spec.Height);
                    _outputs.Add(o);
                }
                o.ClearBatches();
                foreach (var (source, verts) in spec.SliceBatches)
                {
                    ID3D11Buffer? vb = null; int count = 0;
                    UploadVertices(ref vb, ref count, verts);
                    if (vb != null) o.SliceBatches.Add((source, vb, count));
                }
                UploadVertices(ref o.MaskVb, ref o.MaskCount, spec.MaskVertices);
            }
        }

        lock (_pendingResizes)
        {
            foreach (var (hwnd, size) in _pendingResizes)
            {
                var o = _outputs.FirstOrDefault(x => x.Hwnd == hwnd);
                if (o == null || (o.Width == size.W && o.Height == size.H)) continue;
                _ctx!.ClearState();
                o.Rtv?.Dispose();
                o.SwapChain!.ResizeBuffers(2, (uint)size.W, (uint)size.H, Format.B8G8R8A8_UNorm, SwapChainFlags.None).CheckError();
                o.Width = size.W; o.Height = size.H;
                using var bb = o.SwapChain.GetBuffer<ID3D11Texture2D>(0);
                o.Rtv = _device!.CreateRenderTargetView(bb);
            }
            _pendingResizes.Clear();
        }
    }

    void CreateSwapChain(OutputTarget o, int width, int height)
    {
        o.Width = Math.Max(1, width);
        o.Height = Math.Max(1, height);
        o.SwapChain = _factory!.CreateSwapChainForHwnd(_device, o.Hwnd, new SwapChainDescription1
        {
            Width = (uint)o.Width,
            Height = (uint)o.Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore,
        });
        _factory.MakeWindowAssociation(o.Hwnd, WindowAssociationFlags.IgnoreAltEnter);
        using var bb = o.SwapChain.GetBuffer<ID3D11Texture2D>(0);
        o.Rtv = _device!.CreateRenderTargetView(bb);
    }

    void UploadVertices(ref ID3D11Buffer? vb, ref int count, float[] data)
    {
        vb?.Dispose();
        vb = null;
        count = data.Length / MappingMesh.FloatsPerVertex;
        if (count == 0) return;
        vb = _device!.CreateBuffer(data, BindFlags.VertexBuffer);
    }

    /// <summary>Desenha os slices e máscaras de cada tela e apresenta.</summary>
    void RenderOutputs(RenderTexture comp, Dictionary<int, RenderTexture> groups, float time)
    {
        var ctx = _ctx!;
        bool first = true;
        foreach (var o in _outputs)
        {
            if (o.Rtv == null) continue;
            SetFrameParams(o.Width, o.Height, time);
            ctx.PSSetShaderResource(0, null!);
            ctx.OMSetRenderTargets(o.Rtv);
            ctx.RSSetViewport(new Viewport(o.Width, o.Height));
            ctx.ClearRenderTargetView(o.Rtv, new Color4(0, 0, 0, 1));

            ctx.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            ctx.IASetInputLayout(_sliceLayout);
            ctx.VSSetShader(_vsSlice);
            ctx.PSSetConstantBuffer(0, _frameCb);
            ctx.PSSetSampler(0, _sampler);

            uint stride = MappingMesh.FloatsPerVertex * sizeof(float);
            foreach (var (source, vb, count) in o.SliceBatches)
            {
                var tex = source < 0 ? comp : groups.GetValueOrDefault(source);
                if (tex == null) continue;
                ctx.IASetVertexBuffer(0, vb, stride);
                ctx.PSSetShader(_psSlice);
                ctx.PSSetShaderResource(0, tex.Srv);
                ctx.Draw((uint)count, 0);
            }
            if (o.MaskVb != null)
            {
                ctx.IASetVertexBuffer(0, o.MaskVb, stride);
                ctx.PSSetShader(_psMask);
                ctx.Draw((uint)o.MaskCount, 0);
            }
            ctx.IASetInputLayout(null);
            ctx.PSSetShaderResource(0, null!);

            // só a primeira tela espera o vsync; as outras apresentam na hora
            o.SwapChain!.Present(first ? 1u : 0u, PresentFlags.None);
            first = false;
        }
        if (_outputs.Count == 0) Thread.Sleep(16);
    }

    void UpdatePreview(RenderTexture comp, float time) => RunPreview(_mainPreview!, comp, time, Preview);

    /// <summary>
    /// Imagem do retorno: com "espelhar", é o próprio telão; senão, uma composição só com as layers
    /// marcadas "no retorno" — e nada quando nenhuma delas está tocando.
    /// </summary>
    void UpdateStageFeed(RenderTexture mainComp, float time)
    {
        if (!StageFeedWanted || StagePreview == null) return;
        if (StageMirror)
        {
            RunPreview(_stagePreview!, mainComp, time, StagePreview);
            return;
        }
        if (!Layers.Any(s => s.ToStage && s.Opacity > 0 && (s.Current is { Ready: true } || s.Outgoing != null))) return;
        if (Environment.TickCount64 - _stagePreview!.Last < 66 && !_stagePreview.Pending) return; // só compõe quando vai usar
        for (int i = 0; i < 2; i++) _stageComp[i] ??= new RenderTexture(_device!, CompWidth, CompHeight);
        int r = Compose(time, _stageComp, s => s.ToStage, main: false);
        RunPreview(_stagePreview, _stageComp[r]!, time, StagePreview);
    }

    unsafe void RunPreview(PreviewChannel ch, RenderTexture comp, float time, Action<byte[], int, int, int>? sink)
    {
        var ctx = _ctx!;
        if (ch.Pending)
        {
            ch.Pending = false;
            var map = ctx.Map(ch.Staging, 0, MapMode.Read);
            int pitch = (int)map.RowPitch;
            var bytes = new byte[pitch * PreviewHeight];
            new ReadOnlySpan<byte>((void*)map.DataPointer, bytes.Length).CopyTo(bytes);
            ctx.Unmap(ch.Staging, 0);
            sink?.Invoke(bytes, PreviewWidth, PreviewHeight, pitch);
        }

        long now = Environment.TickCount64;
        if (sink == null || now - ch.Last < 66) return; // ~15 fps
        ch.Last = now;

        SetFrameParams(PreviewWidth, PreviewHeight, time);
        ctx.RSSetViewport(new Viewport(PreviewWidth, PreviewHeight));
        Pass(_psBlit!, ch.Rt.Rtv, comp.Srv);
        ctx.PSSetShaderResource(0, null!);
        ctx.CopyResource(ch.Staging, ch.Rt.Texture);
        ch.Pending = true;
    }
    void DisposeOutputs()
    {
        foreach (var o in _outputs) o.Dispose();
        _outputs.Clear();
        _mainPreview?.Dispose();
        _stagePreview?.Dispose();
        foreach (var t in _stageComp) t?.Dispose();
        _sliceLayout?.Dispose();
        _psBlit?.Dispose();
        _psMask?.Dispose();
        _psSlice?.Dispose();
        _vsSlice?.Dispose();
        _factory?.Dispose();
    }
}
