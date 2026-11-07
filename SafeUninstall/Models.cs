namespace SafeUninstall;

enum SafetyLevel
{
    Required,
    Keep,
    YourFiles,
    Uninstallable,
    Review,
    SafeToDelete
}

enum DeleteKind
{
    Junk,
    Leftover,
    TestSandbox
}

sealed class InstalledApp
{
    public required string DisplayName { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public long SizeBytes { get; init; } = -1;
    public string InstallDate { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string UninstallString { get; init; } = "";
    public string RegistryKeyName { get; init; } = "";
    public bool WindowsInstaller { get; init; }
    public bool SilentOnly { get; init; }
    public bool IsSystemComponent { get; init; }
    public bool IsWindowsUpdate { get; init; }
    public bool IsStore { get; init; }
    public bool IsFramework { get; init; }
    public bool NonRemovable { get; init; }
    public string PackageFullName { get; init; } = "";
    public SafetyLevel Safety { get; init; }
    public string Reason { get; init; } = "";
    public bool HideByDefault { get; init; }
    public bool CanUninstall { get; init; }
    public string UninstallBlock { get; init; } = "";
}

sealed class DeleteReport
{
    public long DeletedFiles { get; set; }
    public long DeletedBytes { get; set; }
    public long Skipped { get; set; }
    public bool EmptiedRecycleBin { get; set; }
    public List<string> Errors { get; } = new();
}
