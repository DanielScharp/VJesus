using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>
/// Envia a composição como sender Spout (protocolo do Spout 2, textura D3D11 compartilhada).
/// Outros programas (OBS com plugin Spout, Resolume, TouchDesigner, MadMapper...) recebem sem cópia pela CPU.
/// Protocolo: o nome do sender entra na lista "SpoutSenderNames" e um mapa com o mesmo nome guarda
/// o handle compartilhado, tamanho e formato da textura.
/// </summary>
sealed class SpoutSender : IDisposable
{
    const string NamesMap = "SpoutSenderNames";
    const int NameLength = 256;
    const int InfoSize = 280; // SharedTextureInfo: 5 × uint + wchar[128] + uint

    readonly string _name;
    readonly ID3D11Texture2D _texture;
    readonly MemoryMappedFile _infoMap;
    readonly MemoryMappedFile _namesMap;
    readonly Mutex _namesMutex;
    readonly int _maxSenders;

    public string Name => _name;

    public SpoutSender(ID3D11Device device, string name, int width, int height)
    {
        _name = name;
        _texture = device.CreateTexture2D(new Texture2DDescription(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.ShaderResource | BindFlags.RenderTarget, ResourceUsage.Default, CpuAccessFlags.None,
            1, 0, ResourceOptionFlags.Shared));

        using var res = _texture.QueryInterface<IDXGIResource>();
        uint handle = (uint)res.SharedHandle.ToInt64(); // handles legados do DXGI cabem em 32 bits

        // informações do sender
        _infoMap = MemoryMappedFile.CreateOrOpen(name, InfoSize);
        using (var view = _infoMap.CreateViewAccessor())
        {
            view.Write(0, handle);
            view.Write(4, (uint)width);
            view.Write(8, (uint)height);
            view.Write(12, (uint)Format.B8G8R8A8_UNorm);
            view.Write(16, 0u); // usage
            var desc = Encoding.Unicode.GetBytes(AppInfo.Name);
            view.WriteArray(20, desc, 0, desc.Length);
            view.Write(276, 0u); // partnerId
        }

        // registra o nome na lista de senders
        _maxSenders = ReadMaxSenders();
        _namesMutex = new Mutex(false, NamesMap + "_mutex");
        _namesMap = MemoryMappedFile.CreateOrOpen(NamesMap, _maxSenders * NameLength);
        EditNames(names => { if (!names.Contains(name)) names.Add(name); });
        SetActiveSender(name, handle, width, height);
    }

    /// <summary>Copia a composição para a textura compartilhada (chamado no render thread).</summary>
    public void Send(ID3D11DeviceContext ctx, ID3D11Texture2D source)
    {
        ctx.CopyResource(_texture, source);
        ctx.Flush(); // garante que o outro processo veja o quadro
    }

    static int ReadMaxSenders()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Leading Edge\Spout");
            if (key?.GetValue("MaxSenders") is int n && n > 0) return n;
        }
        catch { }
        return 64;
    }

    void EditNames(Action<List<string>> edit)
    {
        bool locked = false;
        try
        {
            locked = _namesMutex.WaitOne(1000);
            using var view = _namesMap.CreateViewAccessor();
            var names = new List<string>();
            var buf = new byte[NameLength];
            for (int i = 0; i < _maxSenders; i++)
            {
                view.ReadArray(i * NameLength, buf, 0, NameLength);
                int len = Array.IndexOf(buf, (byte)0);
                if (len <= 0) break;
                names.Add(Encoding.ASCII.GetString(buf, 0, len));
            }
            edit(names);
            var zero = new byte[NameLength];
            for (int i = 0; i < _maxSenders; i++)
            {
                view.WriteArray(i * NameLength, zero, 0, NameLength);
                if (i < names.Count)
                {
                    var bytes = Encoding.ASCII.GetBytes(names[i]);
                    view.WriteArray(i * NameLength, bytes, 0, Math.Min(bytes.Length, NameLength - 1));
                }
            }
        }
        finally
        {
            if (locked) _namesMutex.ReleaseMutex();
        }
    }

    // "ActiveSenderName": o sender que receptores usam por padrão
    MemoryMappedFile? _activeMap;
    void SetActiveSender(string name, uint handle, int width, int height)
    {
        try
        {
            _activeMap = MemoryMappedFile.CreateOrOpen("ActiveSenderName", NameLength);
            using var view = _activeMap.CreateViewAccessor();
            var bytes = new byte[NameLength];
            Encoding.ASCII.GetBytes(name).CopyTo(bytes, 0);
            view.WriteArray(0, bytes, 0, NameLength);
        }
        catch { }
    }

    public void Dispose()
    {
        try { EditNames(names => names.Remove(_name)); } catch { }
        _activeMap?.Dispose();
        _namesMap.Dispose();
        _namesMutex.Dispose();
        _infoMap.Dispose();
        _texture.Dispose();
    }
}
