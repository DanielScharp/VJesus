using System.Text.Json;
using System.Text.Json.Nodes;
using VJApp.Engine;
using VJApp.Model;

namespace VJApp;

/// <summary>Ataques visuais (flash, blackout, strobe, zoom…) disparados por botão, tecla, MIDI, OSC ou celular.</summary>
sealed partial class MainForm
{
    AttackLibrary _attackLib = AttackLibrary.LoadOrDefault(AttackLibrary.DefaultPath);

    void OnAttackMessage(string type, JsonNode msg)
    {
        switch (type)
        {
            case "attackFire": FireAttack((string?)msg["id"]); break;
            case "attackRelease": ReleaseAttack((string?)msg["id"]); break;
            case "attackStopAll": _renderer?.StopAllAttacks(); break;
            case "attacksSave":
                if (msg["attacks"].Deserialize<List<AttackPreset>>(JsonRead) is { } list)
                {
                    _attackLib.Attacks = list;
                    _attackLib.Save(AttackLibrary.DefaultPath);
                    SendAttacks();
                    BroadcastRemoteState();
                }
                break;
            case "attacksReset":
                _attackLib.Attacks = AttackLibrary.Defaults();
                _attackLib.Save(AttackLibrary.DefaultPath);
                SendAttacks();
                break;
        }
    }

    void FireAttack(string? id)
    {
        var a = _attackLib.Attacks.FirstOrDefault(x => x.Id == id);
        if (a == null || _renderer == null) return;
        var c = ParseHex(a.Color);
        _renderer.FireAttack(a.Id, new AttackSpec((int)a.Type, c.R, c.G, c.B,
            Math.Max(0, a.AttackMs) / 1000f, Math.Max(0, a.HoldMs) / 1000f, Math.Max(0, a.ReleaseMs) / 1000f,
            Math.Clamp(a.Intensity, 0, 1), a.Amount, Math.Max(1, a.Count), a.HoldWhilePressed));
    }

    void ReleaseAttack(string? id)
    {
        if (id != null) _renderer?.ReleaseAttack(id);
    }

    /// <summary>OSC/MIDI: /attack/{n} (1 = primeiro da lista). Valor ≥ 0.5 dispara, &lt; 0.5 solta (modo segurar).</summary>
    void AttackByIndex(int index, bool press)
    {
        if (index < 0 || index >= _attackLib.Attacks.Count) return;
        var id = _attackLib.Attacks[index].Id;
        if (press) FireAttack(id); else ReleaseAttack(id);
    }

    void SendAttacks() => Send(new { type = "attacks", list = _attackLib.Attacks });

    static (float R, float G, float B) ParseHex(string hex)
    {
        try
        {
            var c = ColorTranslator.FromHtml(hex.Trim());
            return (c.R / 255f, c.G / 255f, c.B / 255f);
        }
        catch { return (1, 1, 1); }
    }
}
