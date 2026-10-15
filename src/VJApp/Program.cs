namespace VJApp;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.FirstOrDefault(a => a.EndsWith(".vjproj", StringComparison.OrdinalIgnoreCase) && File.Exists(a)) is { } proj)
            form.StartupProject = Path.GetFullPath(proj);
        Application.Run(form);
    }
}
