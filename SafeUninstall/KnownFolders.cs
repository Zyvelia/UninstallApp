using System.Runtime.InteropServices;

namespace SafeUninstall;

static class KnownFolders
{
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public static string Windows => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static string System => Environment.SystemDirectory;
    public static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    public static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    public static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    public static string Documents => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    public static string Pictures => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    public static string Music => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public static string Videos => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public static string StartMenuPrograms => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    public static string CommonPrograms => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
    public static string Temp => Path.GetTempPath();
    public static string WindowsTemp => Path.Combine(Windows, "Temp");

    public static string LocalLow => Path.Combine(UserProfile, "AppData", "LocalLow");

    public static string Downloads
    {
        get
        {
            var id = DownloadsId;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero)
                return Path.Combine(UserProfile, "Downloads");
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? Path.Combine(UserProfile, "Downloads");
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    public static IReadOnlyList<string> OneDrive()
    {
        var paths = new List<string>();
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                paths.Add(value);
        }

        return paths;
    }

    public static IReadOnlyList<string> Personal()
    {
        var paths = new List<string>
        {
            Desktop,
            Documents,
            Pictures,
            Music,
            Videos,
            Downloads,
            Path.Combine(UserProfile, "Saved Games"),
            Path.Combine(UserProfile, "3D Objects"),
            Path.Combine(UserProfile, "Favorites"),
            Path.Combine(UserProfile, "Contacts"),
            Path.Combine(UserProfile, "Links")
        };
        paths.AddRange(OneDrive());
        return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
