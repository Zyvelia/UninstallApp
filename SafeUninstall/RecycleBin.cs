using System.Runtime.InteropServices;

namespace SafeUninstall;

static class RecycleBin
{
    const uint NoConfirmation = 0x00000001;
    const uint NoSound = 0x00000004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShQueryRbInfo
    {
        public int cbSize;
        public long Size;
        public long Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHQueryRecycleBin(string? root, ref ShQueryRbInfo info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    public static bool TryMeasure(out long bytes, out long items)
    {
        bytes = 0;
        items = 0;
        if (TryQuery(Marshal.SizeOf<ShQueryRbInfo>(), out bytes, out items))
            return true;
        return TryQuery(20, out bytes, out items);
    }

    public static int Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, NoConfirmation | NoSound);

    public static bool Succeeded(int hr) => hr >= 0;

    static bool TryQuery(int size, out long bytes, out long items)
    {
        bytes = 0;
        items = 0;
        var info = new ShQueryRbInfo { cbSize = size };
        var hr = SHQueryRecycleBin(null, ref info);
        if (hr < 0 || info.Size < 0 || info.Items < 0)
            return false;
        if (info.Size > 200L * 1024 * 1024 * 1024 * 1024)
            return false;
        bytes = info.Size;
        items = info.Items;
        return true;
    }
}
