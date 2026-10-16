namespace VJApp;

/// <summary>Nome do programa e pasta das configurações.</summary>
static class AppInfo
{
    public const string Name = "VJesus";

    /// <summary>%AppData%\VJesus: composição, mapeamento, letras, Bíblias, fontes, atalhos…</summary>
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);

    /// <summary>Quem usava o programa com o nome antigo (VJApp) continua com tudo: a pasta é renomeada uma vez.</summary>
    public static void MigrateOldData()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VJApp");
            if (Directory.Exists(old) && !Directory.Exists(DataDir)) Directory.Move(old, DataDir);
        }
        catch { /* pasta em uso: tenta de novo na próxima abertura */ }
    }
}
