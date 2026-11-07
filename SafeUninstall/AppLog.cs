namespace SafeUninstall;

static class AppLog
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SafeUninstall");

    public static string FilePath => Path.Combine(DirectoryPath, "log.txt");

    public static void Error(Exception ex) => Write(ex.ToString());

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
