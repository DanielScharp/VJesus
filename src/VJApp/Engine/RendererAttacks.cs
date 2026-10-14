using System.Runtime.InteropServices;
using Vortice.Direct3D11;

namespace VJApp.Engine;

[StructLayout(LayoutKind.Sequential)]
struct AttackParams
{
    public float Type;
    public float Envelope;   // 0..1: força do ataque agora
    public float Intensity;
    public float Amount;
    public float R, G, B;
    public float Progress;   // 0..1 desde o disparo até o fim (onda, varredura)
    public float Count;      // strobe: piscadas
    public float Elapsed;    // segundos desde o disparo
    public float Seed;
    public float Pad;
}

/// <summary>Configuração de um ataque disparado (tempos em segundos).</summary>
sealed record AttackSpec(int Type, float R, float G, float B, float Attack, float Hold, float Release,
    float Intensity, float Amount, int Count, bool Sustain);

/// <summary>Ataques: efeitos rápidos por cima da composição final (antes das telas, Spout/NDI e preview).</summary>
partial class Renderer
{
    sealed class ActiveAttack(string id, AttackSpec spec, double start)
    {
        public readonly string Id = id;
        public readonly AttackSpec Spec = spec;
        public readonly double Start = start;
        public readonly float Seed = Random.Shared.NextSingle() * 100;
        /// <summary>Modo segurar: quando soltou (null = ainda pressionado).</summary>
        public double? ReleasedAt;
    }

    readonly List<ActiveAttack> _attacks = [];
    ID3D11PixelShader? _psAttack;
    ID3D11Buffer? _attackCb;

    /// <summary>Dispara um ataque. Com Sustain, ele segura até <see cref="ReleaseAttack"/>.</summary>
    public void FireAttack(string id, AttackSpec spec)
    {
        lock (_attacks)
        {
            // disparar de novo o mesmo ataque reinicia (não acumula)
            _attacks.RemoveAll(a => a.Id == id);
            if (_attacks.Count >= 8) _attacks.RemoveAt(0);
            _attacks.Add(new ActiveAttack(id, spec, _clock.Elapsed.TotalSeconds));
        }
    }

    public void ReleaseAttack(string id)
    {
        lock (_attacks)
            foreach (var a in _attacks.Where(a => a.Id == id && a.ReleasedAt == null))
                a.ReleasedAt = _clock.Elapsed.TotalSeconds;
    }

    public void StopAllAttacks()
    {
        lock (_attacks) _attacks.Clear();
    }

    void InitAttacks(string src, string path)
    {
        _psAttack = _device!.CreatePixelShader(Vortice.D3DCompiler.Compiler.Compile(src, "PSAttack", path, "ps_5_0").Span);
        _attackCb = CreateCb<AttackParams>();
    }

    /// <summary>Envelope entra → segura → sai. Retorna -1 quando o ataque acabou.</summary>
    static float Envelope(ActiveAttack a, double now, out float progress)
    {
        var s = a.Spec;
        double t = now - a.Start;
        double end;
        double env;
        if (s.Sustain && a.ReleasedAt == null)
        {
            env = s.Attack > 0 ? Math.Min(1, t / s.Attack) : 1;
            progress = (float)((t % Math.Max(0.05, s.Attack + s.Hold + s.Release)) / Math.Max(0.05, s.Attack + s.Hold + s.Release));
            return (float)env;
        }
        if (s.Sustain && a.ReleasedAt is { } rel)
        {
            double r = now - rel;
            if (r >= s.Release) { progress = 1; return -1; }
            env = s.Release > 0 ? 1 - r / s.Release : 0;
            progress = (float)Math.Min(1, t / Math.Max(0.05, s.Attack + s.Hold + s.Release));
            return (float)(env * env * (3 - 2 * env));
        }
        end = s.Attack + s.Hold + s.Release;
        if (t >= end) { progress = 1; return -1; }
        progress = (float)(t / Math.Max(0.001, end));
        if (t < s.Attack) env = t / s.Attack;
        else if (t < s.Attack + s.Hold) env = 1;
        else
        {
            double r = s.Release > 0 ? 1 - (t - s.Attack - s.Hold) / s.Release : 0;
            env = r * r * (3 - 2 * r); // saída suave
        }
        return (float)env;
    }

    /// <summary>Aplica os ataques ativos sobre a composição; retorna o índice com o resultado.</summary>
    int ApplyAttacks(RenderTexture?[] pair, int cur, float time)
    {
        ActiveAttack[] list;
        lock (_attacks)
        {
            if (_attacks.Count == 0) return cur;
            list = [.. _attacks];
        }
        double now = _clock.Elapsed.TotalSeconds;
        SetFrameParams(CompWidth, CompHeight, time);
        _ctx!.RSSetViewport(new Vortice.Mathematics.Viewport(CompWidth, CompHeight));
        foreach (var a in list)
        {
            float env = Envelope(a, now, out float progress);
            if (env < 0)
            {
                lock (_attacks) _attacks.Remove(a);
                continue;
            }
            var s = a.Spec;
            var p = new AttackParams
            {
                Type = s.Type, Envelope = env, Intensity = s.Intensity, Amount = s.Amount,
                R = s.R, G = s.G, B = s.B, Progress = progress, Count = s.Count,
                Elapsed = (float)(now - a.Start), Seed = a.Seed,
            };
            _ctx.UpdateSubresource(in p, _attackCb!);
            _ctx.PSSetConstantBuffer(3, _attackCb);
            int next = 1 - cur;
            Pass(_psAttack!, pair[next]!.Rtv, pair[cur]!.Srv);
            cur = next;
        }
        return cur;
    }

    void DisposeAttacks()
    {
        _psAttack?.Dispose();
        _attackCb?.Dispose();
    }
}
