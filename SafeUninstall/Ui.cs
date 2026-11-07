namespace SafeUninstall;

static class Ui
{
    public static readonly Color Header = Color.FromArgb(16, 42, 67);
    public static readonly Color Page = Color.FromArgb(247, 248, 250);
    public static readonly Color Ink = Color.FromArgb(15, 23, 42);
    public static readonly Color Muted = Color.FromArgb(71, 85, 105);
    public static readonly Color Line = Color.FromArgb(226, 232, 240);
    public static readonly Color Accent = Color.FromArgb(31, 111, 235);
    public static readonly Color DangerColor = Color.FromArgb(180, 35, 24);
    public static readonly Font TitleFont = new("Segoe UI Semibold", 20f, FontStyle.Bold);
    public static readonly Font StrongFont = new("Segoe UI Semibold", 9f, FontStyle.Bold);
    static readonly Font CellFont = new("Segoe UI", 9.5f);

    public static Button Button(string text, Color back, Color fore)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 6, 14, 6),
            Margin = new Padding(0, 10, 8, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            Cursor = Cursors.Hand,
            Font = StrongFont,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = back == Color.White ? 1 : 0;
        button.FlatAppearance.BorderColor = Line;
        return button;
    }

    public static Button Primary(string text) => Button(text, Accent, Color.White);
    public static Button Secondary(string text) => Button(text, Color.White, Ink);
    public static Button Danger(string text) => Button(text, DangerColor, Color.White);

    public static TextBox SearchBox(string cue, int width)
    {
        var box = new TextBox
        {
            Width = width,
            Margin = new Padding(8, 14, 8, 8)
        };
        box.HandleCreated += (_, _) => Cue(box, cue);
        return box;
    }

    public static CheckBox Check(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(8, 16, 8, 8),
        ForeColor = Ink
    };

    public static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Top,
        Height = 52,
        Padding = new Padding(12, 0, 12, 0),
        BackColor = Color.White,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight
    };

    public static TextBox DetailBox() => new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Color.FromArgb(255, 251, 235),
        ForeColor = Ink,
        Font = CellFont,
        TabStop = false,
        Text = ""
    };

    public static Panel DetailHost(TextBox box)
    {
        var host = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 108,
            BackColor = Color.FromArgb(255, 251, 235),
            Padding = new Padding(12, 8, 12, 8)
        };
        host.Controls.Add(box);
        return host;
    }

    public static void StyleGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = Color.White;
        grid.BorderStyle = BorderStyle.None;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.EnableHeadersVisualStyles = false;
        grid.ReadOnly = true;
        grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        grid.ColumnHeadersDefaultCellStyle.Font = StrongFont;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(241, 245, 249);
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Font = CellFont;
        grid.DefaultCellStyle.ForeColor = Ink;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
        grid.DefaultCellStyle.SelectionForeColor = Ink;
        grid.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        grid.GridColor = Line;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.RowTemplate.Height = 34;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText;
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellFormatting += (_, e) => FormatCell(grid, e);
        typeof(DataGridView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(grid, true);
    }

    public static DataGridViewTextBoxColumn Column(string name, string header, float weight, int min = 70) => new()
    {
        Name = name,
        HeaderText = header,
        FillWeight = weight,
        MinimumWidth = min,
        SortMode = DataGridViewColumnSortMode.Automatic,
        ReadOnly = true
    };

    static void FormatCell(DataGridView grid, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
            return;
        var name = grid.Columns[e.ColumnIndex].Name;
        if ((name is "Size" or "SafeCache") && e.Value is long size)
        {
            e.Value = size < -1 ? "Folder" : SizeText.Format(size);
            e.FormattingApplied = true;
        }
        else if (name == "Files" && e.Value is long count)
        {
            e.Value = count < 0 ? "Unknown" : count.ToString("N0");
            e.FormattingApplied = true;
        }

        if (name is "Safety" or "Advice" && e.Value is string text)
        {
            e.CellStyle.ForeColor = SafetyText.AdviceColor(text);
            e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
            e.CellStyle.Font = StrongFont;
        }
    }

    static void Cue(TextBox box, string cue)
    {
        _ = SendMessage(box.Handle, 0x1501, (IntPtr)1, cue);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}
