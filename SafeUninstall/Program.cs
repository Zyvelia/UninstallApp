namespace SafeUninstall;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
            return SelfTest.Run();

        if (args.Any(arg => arg.Equals("--dump-junk", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = new List<string>();
            foreach (var category in JunkCatalog.Create())
            {
                JunkCatalog.Measure(category, CancellationToken.None);
                lines.Add(category.Bytes + "\t" + category.FileCount + "\t" + category.AccessDenied + "\t" + category.Name + "\t" + category.Path);
            }

            File.WriteAllText(Path.Combine(Path.GetTempPath(), "SafeUninstall-junk.txt"), string.Join(Environment.NewLine, lines));
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Segoe UI", 9.5f));
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Error(e.Exception);
            MessageBox.Show("Something went wrong.\r\n\r\n" + e.Exception.Message + "\r\n\r\nDetails were saved to " + AppLog.FilePath, "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                AppLog.Error(ex);
        };

        Application.Run(new MainForm());
        return 0;
    }
}
