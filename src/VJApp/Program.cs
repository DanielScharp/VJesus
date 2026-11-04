using System.Diagnostics;

namespace VJApp;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppInfo.MigrateOldData();
        // erro na tela (botão, mensagem): anota e continua; o culto não pode parar por causa de um clique
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogError("tela", e.Exception);
        // erro fatal fora da tela: anota e abre o VJesus de novo, que recoloca no telão o que estava tocando
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogError("fatal", e.ExceptionObject as Exception);
            Relaunch();
        };
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.FirstOrDefault(a => a.EndsWith(".vjproj", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) is { } proj)
            form.StartupProject = Path.GetFullPath(proj);
        Application.Run(form);
    }

    static string ErrorLog => Path.Combine(AppInfo.DataDir, "erros.log");
    static string RelaunchMark => Path.Combine(AppInfo.DataDir, "ultimo-reinicio.txt");

    static void LogError(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({where}) {ex}\n\n");
        }
        catch { }
    }

    /// <summary>Reabre o programa (no máximo uma vez por minuto, para não ficar reabrindo sem parar).</summary>
    static void Relaunch()
    {
        try
        {
            if (File.Exists(RelaunchMark) && DateTime.TryParse(File.ReadAllText(RelaunchMark), out var last)
                && DateTime.Now - last < TimeSpan.FromMinutes(1)) return;
            File.WriteAllText(RelaunchMark, DateTime.Now.ToString("o"));
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--recuperar") { UseShellExecute = true });
        }
        catch { }
    }
}
