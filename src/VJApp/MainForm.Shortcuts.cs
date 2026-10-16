using System.Text.Json;
using System.Text.Json.Nodes;

namespace VJApp;

/// <summary>Atalhos de teclado escolhidos pela pessoa (a lista de ações fica na interface).</summary>
sealed partial class MainForm
{
    static string ShortcutsPath => Path.Combine(AppDataDir, "atalhos.json");

    void OnShortcutsMessage(string type, JsonNode msg)
    {
        if (type != "shortcutsSave") return;
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(ShortcutsPath, msg["shortcuts"]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}");
        }
        catch (Exception ex) { Send(new { type = "error", message = $"Não consegui salvar os atalhos: {ex.Message}" }); }
    }

    void SendShortcuts()
    {
        JsonNode? map = null;
        try { if (File.Exists(ShortcutsPath)) map = JsonNode.Parse(File.ReadAllText(ShortcutsPath)); } catch { /* arquivo ruim: usa os padrões */ }
        Send(new { type = "shortcuts", shortcuts = map });
    }
}
