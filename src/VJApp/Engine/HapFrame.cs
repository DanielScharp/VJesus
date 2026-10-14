using System.Buffers.Binary;
using Vortice.DXGI;

namespace VJApp.Engine;

/// <summary>Como a textura HAP deve ser interpretada no shader.</summary>
enum HapColor { Rgb = 0, Rgba = 1, ScaledYCoCg = 2 }

/// <summary>
/// Lê quadros HAP direto do pacote (sem o decoder do FFmpeg): só tira a compressão Snappy e entrega os
/// blocos DXT/BC prontos para subir na GPU, que descomprime de graça na hora de desenhar.
/// Formato: https://github.com/Vidvox/hap/blob/master/documentation/HapVideoDRAFT.md
/// </summary>
static class HapFrame
{
    public sealed record Info(Format Format, HapColor Color, int BlockBytes);

    /// <summary>Descobre o formato pelo tipo da seção principal. Null = variante não suportada (vai pela CPU).</summary>
    public static Info? Probe(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 8) return null;
        var (type, _, _) = Header(packet);
        return (type & 0x0F) switch
        {
            0x0B => new Info(Format.BC1_UNorm, HapColor.Rgb, 8),          // Hap (DXT1)
            0x0E => new Info(Format.BC3_UNorm, HapColor.Rgba, 16),        // Hap Alpha (DXT5)
            0x0F => new Info(Format.BC3_UNorm, HapColor.ScaledYCoCg, 16), // Hap Q (YCoCg em DXT5)
            0x0C => new Info(Format.BC7_UNorm, HapColor.Rgba, 16),        // Hap R (BC7)
            _ => null,                                                   // Hap Q Alpha, alpha-only etc.
        };
    }

    /// <summary>Descomprime o pacote em <paramref name="output"/> (tamanho exato dos blocos). Retorna false se inválido.</summary>
    public static bool Decode(ReadOnlySpan<byte> packet, Span<byte> output)
    {
        var (type, headerLen, size) = Header(packet);
        if (headerLen + size > packet.Length) return false;
        var payload = packet.Slice(headerLen, size);

        switch (type & 0xF0)
        {
            case 0xA0: // sem compressão
                if (payload.Length < output.Length) return false;
                payload[..output.Length].CopyTo(output);
                return true;
            case 0xB0: // Snappy
                return Snappier.Snappy.Decompress(payload, output) == output.Length;
            case 0xC0: // em pedaços (chunks), cada um com sua compressão
                return DecodeChunks(payload, output);
            default:
                return false;
        }
    }

    static bool DecodeChunks(ReadOnlySpan<byte> payload, Span<byte> output)
    {
        var (instrType, instrHeader, instrSize) = Header(payload);
        if (instrType != 0x01) return false;
        var instr = payload.Slice(instrHeader, instrSize);
        var data = payload[(instrHeader + instrSize)..];

        ReadOnlySpan<byte> compressors = default, sizes = default, offsets = default;
        while (instr.Length >= 4)
        {
            var (t, h, s) = Header(instr);
            var body = instr.Slice(h, s);
            if (t == 0x02) compressors = body;
            else if (t == 0x03) sizes = body;
            else if (t == 0x04) offsets = body;
            instr = instr[(h + s)..];
        }
        int chunks = compressors.Length;
        if (chunks == 0 || sizes.Length < chunks * 4) return false;

        int inPos = 0, outPos = 0;
        for (int i = 0; i < chunks; i++)
        {
            int size = BinaryPrimitives.ReadInt32LittleEndian(sizes[(i * 4)..]);
            int start = offsets.Length >= (i + 1) * 4 ? BinaryPrimitives.ReadInt32LittleEndian(offsets[(i * 4)..]) : inPos;
            var chunk = data.Slice(start, size);
            inPos = start + size;

            if (compressors[i] == 0x0B)
            {
                int n = Snappier.Snappy.Decompress(chunk, output[outPos..]);
                outPos += n;
            }
            else
            {
                chunk.CopyTo(output[outPos..]);
                outPos += chunk.Length;
            }
        }
        return outPos == output.Length;
    }

    /// <summary>Cabeçalho de seção: 3 bytes de tamanho + 1 de tipo; tamanho 0 = tamanho em 4 bytes a seguir.</summary>
    static (int Type, int HeaderLength, int Size) Header(ReadOnlySpan<byte> d)
    {
        int size = d[0] | (d[1] << 8) | (d[2] << 16);
        int type = d[3];
        if (size == 0 && d.Length >= 8) return (type, 8, BinaryPrimitives.ReadInt32LittleEndian(d[4..]));
        return (type, 4, size);
    }
}
