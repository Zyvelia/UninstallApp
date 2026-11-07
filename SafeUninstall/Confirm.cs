namespace SafeUninstall;

static class Confirm
{
    public const string HelpText = """
        Applications
        This list comes from Windows and the Microsoft Store.
        "Safe to uninstall" means the Uninstall button opens that program's own uninstaller.
        "Needed" means removing it can break Windows, a driver, or another app. The button stays off.
        Windows updates and built-in apps are hidden until you uncheck the box.

        Leftover folders
        After an app is uninstalled, this looks for a folder with that exact name.
        Similar names are ignored, so an unrelated folder is not offered.
        Nothing is deleted while the app is still installed.
        Registry entries are never deleted.
        Folders outside Program Files, AppData, ProgramData, and the Start Menu are shown only. This app will not delete them.
        Deleting asks you to type the folder name. That can include that app's settings and saved passwords.

        Junk and caches
        Only the Recycle Bin, temp files, shader caches, thumbnails, and browser cache folders are removed.
        Cookies, passwords, documents, photos, and Windows are not in that list.
        Files a program is using are skipped.

        All files
        Scan reads internal disks and groups every file it can open.
        The advice is for the whole folder. Double-click a folder to look inside.
        This page never deletes files. Safe cache is removed from Junk and caches.

        Nothing here deletes C:\Windows, your Documents, Desktop, or Pictures.
        """;

    public static void ShowHelp(IWin32Window owner)
    {
        using var form = Dialog("How SafeUninstall decides", 680, 560);
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            Text = HelpText,
            Font = new Font("Segoe UI", 10.5f),
            BackColor = Color.White
        };
        form.Controls.Add(text);
        form.ShowDialog(owner);
    }

    public static bool Uninstall(IWin32Window owner, string body)
    {
        using var form = Dialog("Open the official uninstaller", 640, 420);
        var layout = Stack(form);
        layout.Controls.Add(Body(body), 0, 0);
        var buttons = Buttons();
        var ok = Ui.Primary("Open uninstaller");
        var cancel = Ui.Secondary("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 1);
        form.CancelButton = cancel;
        form.AcceptButton = ok;
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static bool Store(IWin32Window owner, string name)
    {
        var body = "Remove this Microsoft Store app for your Windows user?\r\n\r\n"
            + name
            + "\r\n\r\nYou can install it again from the Microsoft Store. Other accounts on this PC are not changed.";
        using var form = Dialog("Remove Store app", 560, 280);
        var layout = Stack(form);
        layout.Controls.Add(Body(body), 0, 0);
        var buttons = Buttons();
        var ok = Ui.Danger("Remove app");
        var cancel = Ui.Secondary("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 1);
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static bool Junk(IWin32Window owner, IReadOnlyList<JunkCategory> items)
    {
        var total = items.Sum(item => Math.Max(item.Bytes, 0));
        using var form = Dialog("Clean temp and cache", 640, 460);
        var layout = Stack(form);
        var intro = "This removes temporary files and caches only.\r\n\r\n"
            + "It does not remove installed programs, documents, photos, or Windows.\r\n"
            + "Files a program is using right now are skipped.\r\n\r\n"
            + "Selected: " + SizeText.Format(total);
        layout.Controls.Add(Body(intro), 0, 0);
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Segoe UI", 9.5f) };
        foreach (var item in items)
            list.Items.Add(item.Name + "    " + SizeText.Format(item.Bytes));
        layout.Controls.Add(list, 0, 1);
        var buttons = Buttons();
        var ok = Ui.Danger("Delete selected");
        var cancel = Ui.Secondary("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 2);
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK;
    }

    public static bool Leftover(IWin32Window owner, LeftoverHit hit)
    {
        var warning = "Delete this leftover folder and everything inside it?\r\n\r\n"
            + hit.Path + "\r\n\r\n"
            + "Size: " + SizeText.Format(hit.Bytes) + "\r\n\r\n"
            + "This can include that app's settings, saves, and saved passwords. "
            + "It does not include your Documents, Desktop, Pictures, or Windows.\r\n\r\n"
            + "Type the folder name to confirm: " + hit.FolderName;
        using var form = Dialog("Delete leftover folder", 680, 460);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(layout);
        layout.Controls.Add(Body(warning), 0, 0);
        var prompt = new Label
        {
            Text = "Folder name",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4)
        };
        var box = new TextBox { Dock = DockStyle.Top, Font = new Font("Segoe UI", 11f) };
        var typed = new Panel { Dock = DockStyle.Top, Height = 64 };
        prompt.Dock = DockStyle.Top;
        box.Dock = DockStyle.Top;
        typed.Controls.Add(box);
        typed.Controls.Add(prompt);
        layout.Controls.Add(typed, 0, 1);
        var buttons = Buttons();
        var ok = Ui.Danger("Delete folder");
        ok.Enabled = false;
        var cancel = Ui.Secondary("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        var name = hit.FolderName;
        box.TextChanged += (_, _) => ok.Enabled = string.Equals(box.Text, name, StringComparison.Ordinal);
        ok.Click += (_, _) =>
        {
            if (string.Equals(box.Text, name, StringComparison.Ordinal))
                form.DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK
            && string.Equals(box.Text, name, StringComparison.Ordinal);
    }

    static Form Dialog(string title, int width, int height) => new()
    {
        Text = title,
        StartPosition = FormStartPosition.CenterParent,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MinimizeBox = false,
        MaximizeBox = false,
        ShowInTaskbar = false,
        ClientSize = new Size(width, height),
        Font = new Font("Segoe UI", 9.5f),
        BackColor = Color.White
    };

    static TableLayoutPanel Stack(Form form)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(16)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        form.Controls.Add(layout);
        return layout;
    }

    static TextBox Body(string text) => new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Vertical,
        Text = text,
        BackColor = Color.White,
        TabStop = false
    };

    static FlowLayoutPanel Buttons() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.RightToLeft,
        AutoSize = true,
        WrapContents = false,
        Padding = new Padding(0, 8, 0, 0)
    };
}
