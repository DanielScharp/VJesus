using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VJApp.Control;

/// <summary>
/// Recebe mensagens OSC por UDP (porta 7000, a mesma do Resolume) e entrega endereço + valor.
/// Aceita argumentos f, i, d, T e F; sem argumento vale 1 (botão). Bundles são abertos.
/// </summary>
sealed class OscServer : IDisposable
{
    readonly UdpClient _udp;
    readonly Action<string, float> _onMessage;
    readonly CancellationTokenSource _cts = new();

    public int Port { get; }

    public OscServer(int port, Action<string, float> onMessage)
    {
        Port = port;
        _onMessage = onMessage;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _ = ReceiveLoop();
    }

    async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(_cts.Token);
                Parse(result.Buffer);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { /* pacote inválido: ignora */ }
        }
    }

    void Parse(ReadOnlySpan<byte> data)
    {
        int pos = 0;
        string first = ReadString(data, ref pos);
        if (first == "#bundle")
        {
            pos += 8; // time tag
            while (pos + 4 <= data.Length)
            {
                int size = BinaryPrimitives.ReadInt32BigEndian(data[pos..]);
                pos += 4;
                if (size <= 0 || pos + size > data.Length) break;
                Parse(data.Slice(pos, size));
                pos += size;
            }
            return;
        }
        if (!first.StartsWith('/')) return;

        float value = 1;
        if (pos < data.Length && data[pos] == (byte)',')
        {
            string tags = ReadString(data, ref pos);
            foreach (char t in tags.AsSpan(1))
            {
                switch (t)
                {
                    case 'f': value = BinaryPrimitives.ReadSingleBigEndian(data[pos..]); pos += 4; break;
                    case 'i': value = BinaryPrimitives.ReadInt32BigEndian(data[pos..]); pos += 4; break;
                    case 'd': value = (float)BinaryPrimitives.ReadDoubleBigEndian(data[pos..]); pos += 8; break;
                    case 'T': value = 1; break;
                    case 'F': value = 0; break;
                    default: continue;
                }
                break; // só o primeiro argumento interessa
            }
        }
        _onMessage(first, value);
    }

    static string ReadString(ReadOnlySpan<byte> data, ref int pos)
    {
        int end = data[pos..].IndexOf((byte)0);
        if (end < 0) end = data.Length - pos;
        string s = Encoding.ASCII.GetString(data.Slice(pos, end));
        pos += (end + 4) & ~3; // strings OSC são alinhadas em 4 bytes
        return s;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp.Dispose();
    }
}
