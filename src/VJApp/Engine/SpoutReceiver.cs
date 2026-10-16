using System.IO.MemoryMappedFiles;
using System.Text;
using Vortice.Direct3D11;

namespace VJApp.Engine;

/// <summary>
/// Recebe a textura de um sender Spout (OBS com plugin Spout, TouchDesigner, Resolume...).
/// Lê o mapa de informações do sender e abre a textura compartilhada no nosso device D3D11,
/// sem cópia pela CPU. Reabre sozinho se o sender mudar de tamanho ou reiniciar.
/// </summary>
sealed class SpoutReceiver(string name) : IDisposable
{
    public string Name { get; } = name;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public ID3D11ShaderResourceView? Srv { get; private set; }

    ID3D11Texture2D? _texture;
    uint _handle;
    long _nextCheck;

    /// <summary>Nomes dos senders Spout ativos (os do próprio VJesus entram também).</summary>
    public static List<string> ListSenders()
    {
        var list = new List<string>();
        try
        {
            using var map = MemoryMappedFile.OpenExisting("SpoutSenderNames");
            using var view = map.CreateViewAccessor();
            var buf = new byte[256];
            for (long pos = 0; pos + 256 <= view.Capacity; pos += 256)
            {
                view.ReadArray(pos, buf, 0, 256);
                int len = Array.IndexOf(buf, (byte)0);
                if (len <= 0) break;
                list.Add(Encoding.ASCII.GetString(buf, 0, len));
            }
        }
        catch { /* nenhum sender Spout rodando */ }
        return list;
    }

    /// <summary>Confere o sender (algumas vezes por segundo) e reabre a textura se mudou. Render thread.</summary>
    public void Update(ID3D11Device device)
    {
        long now = Environment.TickCount64;
        if (now < _nextCheck) return;
        _nextCheck = now + 250;

        uint handle = 0, w = 0, h = 0;
        try
        {
            using var map = MemoryMappedFile.OpenExisting(Name);
            using var view = map.CreateViewAccessor(0, 16);
            handle = view.ReadUInt32(0);
            w = view.ReadUInt32(4);
            h = view.ReadUInt32(8);
        }
        catch { /* sender fechou */ }

        if (handle == _handle && Srv != null) return;
        Release();
        _handle = handle;
        if (handle == 0 || w == 0 || h == 0) return;
        try
        {
            _texture = device.OpenSharedResource<ID3D11Texture2D>((IntPtr)handle);
            Srv = device.CreateShaderResourceView(_texture);
            Width = (int)w;
            Height = (int)h;
        }
        catch
        {
            Release(); // handle inválido (sender em outra GPU, por exemplo)
            _handle = 0;
        }
    }

    void Release()
    {
        Srv?.Dispose(); Srv = null;
        _texture?.Dispose(); _texture = null;
    }

    public void Dispose() => Release();
}
