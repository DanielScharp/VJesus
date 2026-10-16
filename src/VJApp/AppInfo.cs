namespace VJApp;

/// <summary>Nome do programa e pasta das configurações.</summary>
static class AppInfo
{
    public const string Name = "VJesus";

    /// <summary>Ícone do programa (o mesmo do .exe) para as janelas.</summary>
    public static Icon? Icon => _icon ??= Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null;
    static Icon? _icon;

    static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment>? _webEnv;

    /// <summary>
    /// Ambiente do WebView2 com os dados em %LocalAppData%VJesusWebView2 (e não ao lado do .exe): a pasta do
    /// programa fica limpa e o desinstalador remove tudo o que instalou.
    /// </summary>
    public static Task<Microsoft.Web.WebView2.Core.CoreWebView2Environment> WebViewEnvironment() =>
        _webEnv ??= Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name, "WebView2"));

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
