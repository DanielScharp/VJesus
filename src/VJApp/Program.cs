using System.Diagnostics;

namespace VJApp;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppInfo.MigrateOldData();
        // acima dos programas comuns (antivírus, atualizações, navegador): o telão não engasga quando o PC está ocupado
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
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

    internal static void LogError(string where, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataDir);
            // o arquivo não cresce sem fim: passando de 2 MB, guarda só a metade mais nova
            if (File.Exists(ErrorLog) && new FileInfo(ErrorLog).Length > 2_000_000) { var t = File.ReadAllText(ErrorLog); File.WriteAllText(ErrorLog, t[(t.Length / 2)..]); }
            File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({where}) {ex}\n\n");
        }
        catch { }
    }

    /// <summary>Reabre o programa (no máximo uma vez por minuto, para não ficar reabrindo sem parar).</summary>
    internal static bool Relaunch()
    {
        try
        {
            // reabre sempre, mas não em laço: no máximo 3 vezes em 5 minutos (um problema que derruba na hora de abrir)
            var recent = File.Exists(RelaunchMark)
                ? File.ReadAllLines(RelaunchMark).Select(l => DateTime.TryParse(l, out var t) ? t : DateTime.MinValue).Where(t => DateTime.Now - t < TimeSpan.FromMinutes(5)).ToList()
                : [];
            if (recent.Count >= 3) return false;
            recent.Add(DateTime.Now);
            File.WriteAllLines(RelaunchMark, recent.Select(t => t.ToString("o")));
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--recuperar") { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }
}
