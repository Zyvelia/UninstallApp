namespace SafeUninstall;

sealed class JunkCategory
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Reason { get; init; }
    public bool IsRecycleBin { get; init; }
    public bool IncludeSubdirectories { get; init; } = true;
    public string[]? Patterns { get; init; }
    public long Bytes { get; set; } = -1;
    public long FileCount { get; set; } = -1;
    public bool AccessDenied { get; set; }
    public bool Measured { get; set; }
    public bool Partial { get; set; }

    public bool CanClean =>
        Measured && !AccessDenied && (
            (IsRecycleBin && Bytes != 0) ||
            (!IsRecycleBin && Bytes > 0 && FileCount > 0));
}

static class JunkCatalog
{
    public static List<JunkCategory> Create()
    {
        var list = new List<JunkCategory>
        {
            new()
            {
                Name = "Recycle Bin",
                Path = "Recycle Bin",
                IsRecycleBin = true,
                Reason = "Files you already deleted. Emptying it permanently removes them. Your folders are not touched."
            }
        };

        Add(list, "Your temporary files", KnownFolders.Temp,
            "Files programs left in your temp folder. In-use files are skipped. Documents and Downloads are not in here.");
        Add(list, "Windows temporary files", KnownFolders.WindowsTemp,
            "Temp files Windows and installers left behind. This does not touch the rest of Windows. Administrator mode can see more of this folder.");

        Add(list, "Crash dumps", Path.Combine(KnownFolders.LocalAppData, "CrashDumps"),
            "Crash dump files. Apps recreate them only if they crash again.");
        Add(list, "DirectX shader cache", Path.Combine(KnownFolders.LocalAppData, "D3DSCache"),
            "Graphics shader cache. Games rebuild it. It can make the next launch a little slower.");
        Add(list, "Thumbnail and icon cache", Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Windows", "Explorer"),
            "Windows picture and icon thumbnails. Windows rebuilds them. Only thumbnail files in this folder are removed.",
            includeSubdirectories: false,
            patterns: ["thumbcache*.db", "iconcache*.db"]);

        Add(list, "Windows error reports", Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"),
            "Old Windows error reports. They are not needed to run your PC.");
        Add(list, "Windows error report queue", Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue"),
            "Queued Windows error reports. They are not needed to run your PC.");
        Add(list, "Temporary Internet files", Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Windows", "INetCache"),
            "Temporary Internet files. Your Downloads folder is not touched.");

        Add(list, "NVIDIA shader cache", Path.Combine(KnownFolders.LocalAppData, "NVIDIA", "DXCache"),
            "NVIDIA shader cache. It is rebuilt when a game needs it.");
        Add(list, "NVIDIA OpenGL cache", Path.Combine(KnownFolders.LocalAppData, "NVIDIA", "GLCache"),
            "NVIDIA shader cache. It is rebuilt when a game needs it.");
        Add(list, "NVIDIA cache", Path.Combine(KnownFolders.LocalAppData, "NVIDIA Corporation", "NV_Cache"),
            "NVIDIA cache. It is rebuilt when needed.");
        Add(list, "AMD shader cache", Path.Combine(KnownFolders.LocalAppData, "AMD", "DxCache"),
            "AMD shader cache. It is rebuilt when a game needs it.");
        Add(list, "AMD OpenGL cache", Path.Combine(KnownFolders.LocalAppData, "AMD", "GLCache"),
            "AMD shader cache. It is rebuilt when a game needs it.");

        AddChromium(list, Path.Combine(KnownFolders.LocalAppData, "Google", "Chrome", "User Data"), "Chrome");
        AddChromium(list, Path.Combine(KnownFolders.LocalAppData, "Microsoft", "Edge", "User Data"), "Edge");
        AddChromium(list, Path.Combine(KnownFolders.LocalAppData, "BraveSoftware", "Brave-Browser", "User Data"), "Brave");
        AddChromium(list, Path.Combine(KnownFolders.LocalAppData, "Vivaldi", "User Data"), "Vivaldi");
        AddFirefox(list);
        AddDiscord(list, Path.Combine(KnownFolders.RoamingAppData, "discord"));
        AddDiscord(list, Path.Combine(KnownFolders.LocalAppData, "Discord"));
        return list;
    }

    public static IReadOnlyList<string> ApprovedPaths() =>
        Create().Where(category => !category.IsRecycleBin).Select(category => category.Path).ToList();

    public static bool IsApproved(string path)
    {
        if (!PathRules.TryFull(path, out var full))
            return false;
        return ApprovedPaths().Any(root => PathRules.Same(root, full));
    }

    public static void Measure(JunkCategory category, CancellationToken ct)
    {
        if (category.IsRecycleBin)
        {
            if (RecycleBin.TryMeasure(out var bytes, out var items))
            {
                category.Bytes = bytes;
                category.FileCount = items;
            }
            else
            {
                category.Bytes = -1;
                category.FileCount = -1;
            }

            category.Measured = true;
            return;
        }

        if (!Directory.Exists(category.Path))
        {
            category.Bytes = 0;
            category.FileCount = 0;
            category.Measured = true;
            return;
        }

        var measured = FileWalk.Measure(category.Path, ct, category.IncludeSubdirectories, category.Patterns);
        category.AccessDenied = measured.RootDenied;
        category.Partial = measured.Partial;
        category.Bytes = measured.RootDenied ? -1 : measured.Bytes;
        category.FileCount = measured.RootDenied ? -1 : measured.Files;
        category.Measured = true;
    }

    static void Add(List<JunkCategory> list, string name, string path, string reason, bool includeSubdirectories = true, string[]? patterns = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;
        if (list.Any(category => !category.IsRecycleBin && PathRules.Same(category.Path, path)))
            return;
        if (PathRules.IsDriveRoot(path) || PathRules.Same(path, KnownFolders.Windows) || PathRules.Same(path, KnownFolders.UserProfile))
            return;

        list.Add(new JunkCategory
        {
            Name = name,
            Path = Path.GetFullPath(path),
            Reason = reason,
            IncludeSubdirectories = includeSubdirectories,
            Patterns = patterns
        });
    }

    static void AddChromium(List<JunkCategory> list, string userData, string product)
    {
        if (!Directory.Exists(userData))
            return;
        foreach (var profile in SafeDirectories(userData))
        {
            foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
            {
                Add(list, $"{product} cache ({Path.GetFileName(profile)} {cache})", Path.Combine(profile, cache),
                    $"{product} cache. The browser rebuilds it. Cookies, history, and saved passwords are not in this folder.");
            }
        }
    }

    static void AddFirefox(List<JunkCategory> list)
    {
        var profiles = Path.Combine(KnownFolders.LocalAppData, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profiles))
            return;
        foreach (var profile in SafeDirectories(profiles))
        {
            Add(list, "Firefox cache (" + Path.GetFileName(profile) + ")", Path.Combine(profile, "cache2"),
                "Firefox cache. Bookmarks, passwords, and history are not in this folder.");
        }
    }

    static void AddDiscord(List<JunkCategory> list, string discordRoot)
    {
        if (!Directory.Exists(discordRoot))
            return;
        foreach (var cache in new[] { "Cache", "Code Cache", "GPUCache" })
        {
            Add(list, "Discord " + cache, Path.Combine(discordRoot, cache),
                "Discord cache. Discord rebuilds it. Login data is not in this folder.");
        }
    }

    static IEnumerable<string> SafeDirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
