using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>Saídas de rede/compartilhamento da composição: Spout e NDI.</summary>
partial class Renderer
{
    /// <summary>Erro ao ligar Spout/NDI (o pedido é desligado).</summary>
    public event Action<string, string>? StreamError;
    /// <summary>Estado atual: nome do sender Spout e NDI (null = desligado).</summary>
    public event Action<string?, string?>? StreamState;

    volatile string? _wantSpout, _wantNdi;
    volatile bool _streamDirty;
    SpoutSender? _spout;
    NdiSender? _ndi;
    readonly ID3D11Texture2D?[] _ndiStaging = new ID3D11Texture2D?[2];
    int _ndiFrame;

    /// <summary>Liga/desliga as saídas (null ou vazio = desligado).</summary>
    public void SetStreaming(string? spoutName, string? ndiName)
    {
        _wantSpout = string.IsNullOrWhiteSpace(spoutName) ? null : spoutName.Trim();
        _wantNdi = string.IsNullOrWhiteSpace(ndiName) ? null : ndiName.Trim();
        _streamDirty = true;
    }

    void ApplyStreaming()
    {
        if (!_streamDirty) return;
        _streamDirty = false;

        if (_spout?.Name != _wantSpout)
        {
            _spout?.Dispose();
            _spout = null;
            if (_wantSpout != null)
            {
                try { _spout = new SpoutSender(_device!, _wantSpout, CompWidth, CompHeight); }
                catch (Exception ex) { _wantSpout = null; StreamError?.Invoke("Spout", ex.Message); }
            }
        }

        if (_ndi?.Name != _wantNdi)
        {
            _ndi?.Dispose();
            _ndi = null;
            if (_wantNdi != null)
            {
                try
                {
                    _ndi = new NdiSender(_wantNdi, CompWidth, CompHeight);
                    for (int i = 0; i < 2; i++)
                        _ndiStaging[i] ??= _device!.CreateTexture2D(new Texture2DDescription(
                            Format.B8G8R8A8_UNorm, CompWidth, CompHeight, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));
                }
                catch (Exception ex) { _wantNdi = null; StreamError?.Invoke("NDI", ex.Message); }
            }
        }
        StreamState?.Invoke(_spout?.Name, _ndi?.Name);
    }

    void SendStreams(RenderTexture comp)
    {
        _spout?.Send(_ctx!, comp.Texture);

        if (_ndi != null)
        {
            // NDI a 30 fps: copia num quadro, lê da GPU no seguinte (sem travar esperando)
            int k = _ndiFrame++ & 3;
            if (k == 0) _ctx!.CopyResource(_ndiStaging[0]!, comp.Texture);
            else if (k == 1) Read(_ndiStaging[0]!);
            else if (k == 2) _ctx!.CopyResource(_ndiStaging[1]!, comp.Texture);
            else Read(_ndiStaging[1]!);
        }

        void Read(ID3D11Texture2D staging)
        {
            var map = _ctx!.Map(staging, 0, MapMode.Read);
            try { _ndi!.Send(map.DataPointer, (int)map.RowPitch, 30); }
            finally { _ctx.Unmap(staging, 0); }
        }
    }

    void DisposeStreams()
    {
        _spout?.Dispose();
        _ndi?.Dispose();
        foreach (var s in _ndiStaging) s?.Dispose();
    }
}
