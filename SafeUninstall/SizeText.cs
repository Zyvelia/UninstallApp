namespace SafeUninstall;

static class SizeText
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
            return "Unknown";
        if (bytes == 0)
            return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}

static class SafetyText
{
    public static string ForApp(SafetyLevel level) => level switch
    {
        SafetyLevel.Required => "Needed — do not remove",
        SafetyLevel.Keep => "Needed by other apps",
        SafetyLevel.Uninstallable => "Safe to uninstall",
        SafetyLevel.Review => "Review before removing",
        SafetyLevel.YourFiles => "Your files — keep",
        SafetyLevel.SafeToDelete => "Safe to delete",
        _ => "Review before removing"
    };

    public static string ForPath(SafetyLevel level) => level switch
    {
        SafetyLevel.Required => "Needed — do not delete",
        SafetyLevel.Keep => "Needed by other apps",
        SafetyLevel.YourFiles => "Your files — keep",
        SafetyLevel.Uninstallable => "Keep — uninstall the app",
        SafetyLevel.Review => "Review before removing",
        SafetyLevel.SafeToDelete => "Safe to delete",
        _ => "Review before removing"
    };

    public static Color AdviceColor(string text)
    {
        if (text.StartsWith("Needed by", StringComparison.Ordinal))
            return Color.FromArgb(154, 52, 18);
        if (text.StartsWith("Needed", StringComparison.Ordinal))
            return Color.FromArgb(153, 27, 27);
        if (text.StartsWith("Your files", StringComparison.Ordinal))
            return Color.FromArgb(30, 64, 175);
        if (text.StartsWith("Safe to delete", StringComparison.Ordinal) || text.StartsWith("Leftover", StringComparison.Ordinal))
            return Color.FromArgb(4, 120, 87);
        if (text.StartsWith("Safe to uninstall", StringComparison.Ordinal))
            return Color.FromArgb(29, 78, 216);
        if (text.StartsWith("Keep", StringComparison.Ordinal))
            return Color.FromArgb(154, 52, 18);
        if (text.StartsWith("Review", StringComparison.Ordinal))
            return Color.FromArgb(146, 64, 14);
        return Color.FromArgb(15, 23, 42);
    }
}
