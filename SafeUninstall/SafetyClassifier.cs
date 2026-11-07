namespace SafeUninstall;

sealed class FolderSnapshot
{
    public required string Windows { get; init; }
    public required string ProgramFiles { get; init; }
    public required string ProgramFilesX86 { get; init; }
    public required string ProgramData { get; init; }
    public required string LocalAppData { get; init; }
    public required string RoamingAppData { get; init; }
    public required string Temp { get; init; }
    public required string WindowsTemp { get; init; }
    public required string[] Personal { get; init; }

    public static FolderSnapshot Capture() => new()
    {
        Windows = KnownFolders.Windows,
        ProgramFiles = KnownFolders.ProgramFiles,
        ProgramFilesX86 = KnownFolders.ProgramFilesX86,
        ProgramData = KnownFolders.ProgramData,
        LocalAppData = KnownFolders.LocalAppData,
        RoamingAppData = KnownFolders.RoamingAppData,
        Temp = KnownFolders.Temp,
        WindowsTemp = KnownFolders.WindowsTemp,
        Personal = KnownFolders.Personal().ToArray()
    };
}

static class SafetyClassifier
{
    public static (SafetyLevel Level, string Reason) ClassifyPath(string path) =>
        Classify(path, FolderSnapshot.Capture());

    public static bool IsPersonalPath(string path) =>
        IsPersonal(path, FolderSnapshot.Capture());

    public static (SafetyLevel Level, string Reason) Classify(string path, FolderSnapshot snap)
    {
        if (!PathRules.TryFull(path, out var full))
            return (SafetyLevel.Review, "The path could not be read.");

        if (IsPersonal(full, snap))
            return (SafetyLevel.YourFiles, "Your files. Nothing on this page deletes them.");

        var fileName = Path.GetFileName(full);
        if (fileName.Equals("pagefile.sys", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("hiberfil.sys", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("swapfile.sys", StringComparison.OrdinalIgnoreCase))
            return (SafetyLevel.Required, "Windows memory file. Windows manages it.");

        if (HasSegment(full, "$Recycle.Bin"))
            return (SafetyLevel.SafeToDelete, "Recycle Bin. Empty it from Junk and caches. Do not delete the Recycle Bin folder itself.");

        if (HasSegment(full, "System Volume Information"))
            return (SafetyLevel.Required, "Windows restore and drive data. Leave this folder alone.");

        if (IsDriveFolder(full, "Recovery") || IsDriveFolder(full, "Boot") || IsDriveFolder(full, "EFI"))
            return (SafetyLevel.Required, "Boot or recovery files. The PC needs these.");

        if (HasSegment(full, "Windows.old"))
            return (SafetyLevel.Review, "A previous Windows installation. Remove it with Windows Disk Cleanup if you want the space. Do not delete it here.");

        if (IsTempOrCache(full, snap))
            return (SafetyLevel.SafeToDelete, "Temporary or cache data. Programs recreate it. Remove it from Junk and caches.");

        if (PathRules.IsUnder(full, snap.Windows))
            return (SafetyLevel.Required, "Windows system file. The PC needs this.");

        var windowsApps = Path.Combine(snap.ProgramFiles, "WindowsApps");
        var windowsAppsX86 = Path.Combine(snap.ProgramFilesX86, "WindowsApps");
        if (PathRules.IsUnder(full, windowsApps) || PathRules.IsUnder(full, windowsAppsX86))
            return (SafetyLevel.Required, "Protected Microsoft Store apps.");

        var common = Path.Combine(snap.ProgramFiles, "Common Files");
        var commonX86 = Path.Combine(snap.ProgramFilesX86, "Common Files");
        if (PathRules.IsUnder(full, common) || PathRules.IsUnder(full, commonX86))
            return (SafetyLevel.Keep, "Shared program files. Deleting this breaks many apps.");

        if (PathRules.IsUnder(full, Path.Combine(snap.ProgramData, "Package Cache")))
            return (SafetyLevel.Keep, "Installer cache. Removing it can stop repair or uninstall from working.");

        if (PathRules.IsUnder(full, Path.Combine(snap.ProgramData, "Microsoft", "Windows")))
            return (SafetyLevel.Required, "Windows data.");

        if (PathRules.IsUnder(full, snap.ProgramFiles) || PathRules.IsUnder(full, snap.ProgramFilesX86))
            return (SafetyLevel.Uninstallable, "Installed program files. Use the Applications tab. Deleting the folder can leave the app half installed.");

        if (IsAppData(full, snap))
            return (SafetyLevel.Review, "App settings or saves. Keep this if you still use the app. The Leftover folders tab can remove it after the app is uninstalled.");

        return (SafetyLevel.Review, "This is not a known Windows, personal, or temp location. Check it before you delete anything yourself.");
    }

    public static void ClassifyInstalledApp(InstalledApp app, out SafetyLevel level, out string reason, out bool hide, out bool canUninstall, out string block)
    {
        var name = app.DisplayName;
        var publisher = app.Publisher;

        if (app.IsStore && app.NonRemovable)
        {
            level = SafetyLevel.Required;
            reason = "Built into Windows. Windows does not allow this one to be removed.";
            hide = true;
            canUninstall = false;
            block = reason;
            return;
        }

        if (app.IsStore && app.IsFramework)
        {
            level = SafetyLevel.Keep;
            reason = "Microsoft Store framework. Other Store apps need it.";
            hide = true;
            canUninstall = false;
            block = reason;
            return;
        }

        if (app.IsSystemComponent || app.IsWindowsUpdate || IsWindowsUpdateName(name))
        {
            level = SafetyLevel.Required;
            reason = "Windows update or system component. Removing it can break Windows.";
            hide = true;
            canUninstall = false;
            block = reason;
            return;
        }

        if (IsDefender(name, publisher))
        {
            level = SafetyLevel.Required;
            reason = "Windows Security. Leave it installed.";
            hide = false;
            canUninstall = false;
            block = reason;
            return;
        }

        if (IsHardwareDriver(name, publisher))
        {
            level = SafetyLevel.Required;
            reason = "Device driver. Removing it can stop hardware from working.";
            hide = false;
            canUninstall = false;
            block = reason;
            return;
        }

        if (IsSharedRuntime(name))
        {
            level = SafetyLevel.Keep;
            reason = SharedRuntimeReason(name);
            hide = false;
            FinishUninstall(app, level, reason, out canUninstall, out block);
            return;
        }

        if (IsEdge(name))
        {
            level = SafetyLevel.Keep;
            reason = "Windows opens some links and help pages with Edge. You can remove it, but some Windows screens expect it.";
            hide = false;
            FinishUninstall(app, level, reason, out canUninstall, out block);
            return;
        }

        if (IsThirdPartySecurity(name, publisher))
        {
            level = SafetyLevel.Review;
            reason = "Security software. The PC is less protected after you remove it. Windows Security remains unless you turned it off.";
            hide = false;
            FinishUninstall(app, level, reason, out canUninstall, out block);
            return;
        }

        if (IsDriverUtility(name, publisher))
        {
            level = SafetyLevel.Review;
            reason = "Driver helper. Remove it only if you do not use it to update hardware. The driver itself may still be installed.";
            hide = false;
            FinishUninstall(app, level, reason, out canUninstall, out block);
            return;
        }

        level = SafetyLevel.Uninstallable;
        reason = app.IsStore
            ? "Store app. Safe to remove for your Windows user. You can install it again from the Microsoft Store."
            : "Ordinary installed program. Safe to remove through its own uninstaller. Do not delete its folder by hand.";
        hide = false;
        FinishUninstall(app, level, reason, out canUninstall, out block);
    }

    static void FinishUninstall(InstalledApp app, SafetyLevel level, string reason, out bool canUninstall, out string block)
    {
        if (app.IsStore)
        {
            canUninstall = !app.NonRemovable && !app.IsFramework;
            block = canUninstall ? "" : reason;
            return;
        }

        if (app.SilentOnly)
        {
            canUninstall = false;
            block = "This program only registered a silent uninstaller. SafeUninstall will not run that. Remove it from Windows Settings, Apps, if it is listed.";
            return;
        }

        if (string.IsNullOrWhiteSpace(app.UninstallString) && !(app.WindowsInstaller && IsProductCode(app.RegistryKeyName)))
        {
            canUninstall = false;
            block = "No official uninstaller was found. SafeUninstall will not delete its folder.";
            return;
        }

        var plan = UninstallService.Plan(app);
        canUninstall = plan.CanRun;
        block = plan.CanRun ? "" : plan.Message;
        _ = level;
        _ = reason;
    }

    public static bool IsWindowsUpdateName(string name)
    {
        if (name.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.Contains("KB", StringComparison.OrdinalIgnoreCase)
            && name.Contains("Windows", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsSharedRuntime(string name)
    {
        string[] keys =
        [
            "Microsoft Visual C++",
            "Visual C++",
            ".NET Runtime",
            ".NET Host",
            ".NET Targeting Pack",
            ".NET Standard",
            "ASP.NET Core",
            "Windows Desktop Runtime",
            "Microsoft .NET",
            "WebView2",
            "DirectX",
            "Vulkan Run",
            "OpenAL",
            "PhysX",
            "XNA Framework",
            "Microsoft XNA",
            "Universal CRT"
        ];
        return keys.Any(key => name.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    static string SharedRuntimeReason(string name)
    {
        if (name.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
            return "Many apps open web pages with WebView2. Removing it can break them.";
        if (name.Contains("Visual C++", StringComparison.OrdinalIgnoreCase))
            return "Shared Microsoft runtime. Games and apps often stop opening if you remove it.";
        return "Shared runtime. Other programs need this. Remove it only when you know nothing uses it.";
    }

    static bool IsEdge(string name) =>
        name.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Microsoft Edge ", StringComparison.OrdinalIgnoreCase);

    static bool IsDefender(string name, string publisher) =>
        publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)
        && (name.Contains("Defender", StringComparison.OrdinalIgnoreCase) || name.Contains("Windows Security", StringComparison.OrdinalIgnoreCase));

    static bool IsHardwareDriver(string name, string publisher)
    {
        if (IsDriverUtility(name, publisher))
            return false;
        var looksLikeDriver = name.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || name.Contains("chipset", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeDriver)
            return false;
        return IsHardwareVendor(name, publisher);
    }

    static bool IsDriverUtility(string name, string publisher)
    {
        if (!name.Contains("driver", StringComparison.OrdinalIgnoreCase))
            return false;
        return name.Contains("assistant", StringComparison.OrdinalIgnoreCase)
            || name.Contains("booster", StringComparison.OrdinalIgnoreCase)
            || name.Contains("updater", StringComparison.OrdinalIgnoreCase)
            || name.Contains("support assistant", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsHardwareVendor(string name, string publisher)
    {
        string[] vendors = ["NVIDIA", "AMD", "Advanced Micro Devices", "Intel", "Realtek", "Microsoft", "Logitech", "Broadcom", "Qualcomm", "Killer", "MediaTek", "ASMedia", "Conexant"];
        return vendors.Any(vendor =>
            publisher.Contains(vendor, StringComparison.OrdinalIgnoreCase)
            || name.Contains(vendor, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsThirdPartySecurity(string name, string publisher)
    {
        if (publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) && name.Contains("Windows", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] keys = ["antivirus", "anti-virus", "internet security", "endpoint", "malware"];
        return keys.Any(key => name.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsProductCode(string value) =>
        Regex.IsMatch(value, @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$");

    static bool IsPersonal(string full, FolderSnapshot snap)
    {
        foreach (var folder in snap.Personal)
        {
            if (!string.IsNullOrWhiteSpace(folder) && PathRules.IsUnder(full, folder))
                return true;
        }

        var parts = full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var users = Array.FindIndex(parts, part => part.Equals("Users", StringComparison.OrdinalIgnoreCase));
        if (users < 0 || users + 2 >= parts.Length)
            return false;
        return parts[users + 2] is "Documents" or "Desktop" or "Pictures" or "Music" or "Videos" or "Downloads"
            or "OneDrive" or "Saved Games" or "3D Objects" or "Favorites" or "Contacts" or "Links";
    }

    static bool IsTempOrCache(string full, FolderSnapshot snap)
    {
        if (PathRules.IsUnder(full, snap.Temp) || PathRules.IsUnder(full, snap.WindowsTemp))
            return true;
        if (IsUsersLocalChild(full, "Temp"))
            return true;
        if (!IsLocalAppDataArea(full, snap))
            return false;

        if (IsUsersLocalChild(full, "CrashDumps") || PathRules.IsUnder(full, Path.Combine(snap.LocalAppData, "CrashDumps")))
            return true;
        if (IsUsersLocalChild(full, "D3DSCache") || PathRules.IsUnder(full, Path.Combine(snap.LocalAppData, "D3DSCache")))
            return true;
        if (ContainsChain(full, ["NVIDIA", "DXCache"]) || ContainsChain(full, ["NVIDIA", "GLCache"]))
            return true;
        if (ContainsChain(full, ["NVIDIA Corporation", "NV_Cache"]))
            return true;
        if (ContainsChain(full, ["AMD", "DxCache"]) || ContainsChain(full, ["AMD", "GLCache"]))
            return true;
        if (ContainsChain(full, ["Microsoft", "Windows", "INetCache"]))
            return true;
        if (ContainsChain(full, ["Microsoft", "Windows", "WER"]))
            return true;
        if (IsBrowserCache(full))
            return true;
        return IsThumbnailCacheFile(full);
    }

    static bool IsThumbnailCacheFile(string full)
    {
        if (!NamePattern.Matches(full, ["thumbcache*.db", "iconcache*.db"]))
            return false;
        var parent = Path.GetFileName(Path.GetDirectoryName(full) ?? "");
        return parent.Equals("Explorer", StringComparison.OrdinalIgnoreCase)
            && ContainsChain(full, ["Microsoft", "Windows", "Explorer"]);
    }

    static bool IsBrowserCache(string full)
    {
        if (IsChromiumCache(full, "Google", "Chrome"))
            return true;
        if (IsChromiumCache(full, "Microsoft", "Edge"))
            return true;
        if (IsChromiumCache(full, "BraveSoftware", "Brave-Browser"))
            return true;
        if (IsVivaldiCache(full))
            return true;
        return IsFirefoxCache(full);
    }

    static bool IsChromiumCache(string full, string vendor, string product)
    {
        var parts = Split(full);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!parts[i].Equals(vendor, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!parts[i + 1].Equals(product, StringComparison.OrdinalIgnoreCase))
                continue;
            var userData = Array.FindIndex(parts, i, part => part.Equals("User Data", StringComparison.OrdinalIgnoreCase));
            if (userData < 0 || userData + 2 >= parts.Length)
                return false;
            var cache = parts[userData + 2];
            return cache.Equals("Cache", StringComparison.OrdinalIgnoreCase)
                || cache.Equals("Code Cache", StringComparison.OrdinalIgnoreCase)
                || cache.Equals("GPUCache", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    static bool IsVivaldiCache(string full)
    {
        var parts = Split(full);
        var index = Array.FindIndex(parts, part => part.Equals("Vivaldi", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 3 >= parts.Length)
            return false;
        if (!parts[index + 1].Equals("User Data", StringComparison.OrdinalIgnoreCase))
            return false;
        var cache = parts[index + 3];
        return cache.Equals("Cache", StringComparison.OrdinalIgnoreCase)
            || cache.Equals("Code Cache", StringComparison.OrdinalIgnoreCase)
            || cache.Equals("GPUCache", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsFirefoxCache(string full)
    {
        var parts = Split(full);
        for (var i = 0; i < parts.Length - 3; i++)
        {
            if (!parts[i].Equals("Mozilla", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!parts[i + 1].Equals("Firefox", StringComparison.OrdinalIgnoreCase))
                continue;
            var profiles = Array.FindIndex(parts, i, part => part.Equals("Profiles", StringComparison.OrdinalIgnoreCase));
            if (profiles < 0)
                return false;
            return parts.Skip(profiles + 1).Any(part => part.Equals("cache2", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    static bool IsLocalAppDataArea(string full, FolderSnapshot snap)
    {
        if (PathRules.IsUnder(full, snap.LocalAppData))
            return true;
        var parts = Split(full);
        var users = Array.FindIndex(parts, part => part.Equals("Users", StringComparison.OrdinalIgnoreCase));
        if (users < 0 || users + 3 >= parts.Length)
            return false;
        return parts[users + 2].Equals("AppData", StringComparison.OrdinalIgnoreCase)
            && parts[users + 3].Equals("Local", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsUsersLocalChild(string full, string child)
    {
        var parts = Split(full);
        var users = Array.FindIndex(parts, part => part.Equals("Users", StringComparison.OrdinalIgnoreCase));
        if (users < 0 || users + 4 >= parts.Length)
            return false;
        return parts[users + 2].Equals("AppData", StringComparison.OrdinalIgnoreCase)
            && parts[users + 3].Equals("Local", StringComparison.OrdinalIgnoreCase)
            && parts[users + 4].Equals(child, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsAppData(string full, FolderSnapshot snap)
    {
        if (PathRules.IsUnder(full, snap.LocalAppData) || PathRules.IsUnder(full, snap.RoamingAppData))
            return true;
        return HasSegment(full, "AppData");
    }

    static bool ContainsChain(string full, string[] chain)
    {
        var parts = Split(full);
        for (var i = 0; i <= parts.Length - chain.Length; i++)
        {
            var match = true;
            for (var j = 0; j < chain.Length; j++)
            {
                if (!parts[i + j].Equals(chain[j], StringComparison.OrdinalIgnoreCase))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return true;
        }

        return false;
    }

    static bool HasSegment(string full, string segment) =>
        Split(full).Any(part => part.Equals(segment, StringComparison.OrdinalIgnoreCase));

    static bool IsDriveFolder(string full, string name)
    {
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            return false;
        return PathRules.IsUnder(full, Path.Combine(root, name));
    }

    static string[] Split(string full) =>
        full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
}
