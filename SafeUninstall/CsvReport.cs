namespace SafeUninstall;

static class CsvReport
{
    public static string Cell(string value)
    {
        // Quoting alone does not stop spreadsheet formulas in exported text.
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])
            || value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
