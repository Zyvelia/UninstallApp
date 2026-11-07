namespace SafeUninstall;

sealed class MainForm : Form
{
    readonly DataGridView _appsGrid = new();
    readonly DataGridView _junkGrid = new();
    readonly DataGridView _fileGrid = new();
    readonly DataGridView _browseGrid = new();
    readonly DataGridView _leftGrid = new();
    readonly TextBox _appSearch;
    readonly TextBox _fileSearch;
    readonly TextBox _leftSearch;
    readonly TextBox _appDetail = Ui.DetailBox();
    readonly TextBox _junkDetail = Ui.DetailBox();
    readonly TextBox _fileDetail = Ui.DetailBox();
    readonly TextBox _leftDetail = Ui.DetailBox();
    readonly CheckBox _hideSystem;
    readonly CheckBox _includeUsb;
    readonly ComboBox _fileFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(8, 14, 8, 8) };
    readonly Label _fileSummary = new();
    readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripProgressBar _progress = new() { Width = 180, Visible = false, Style = ProgressBarStyle.Marquee };
    readonly List<Button> _actions = new();
    readonly Stack<string> _browseStack = new();

    List<InstalledApp> _apps = new();
    List<JunkCategory> _junk = new();
    List<BrowseEntry> _browse = new();
    FileScanResult? _fileScan;
    CancellationTokenSource? _cts;
    bool _busy;
    Button? _uninstallButton;
    Button? _deleteJunkButton;
    Button? _deleteLeftButton;
    Button? _backButton;

    public MainForm()
    {
        Text = "SafeUninstall";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Ui.Page;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        Width = Math.Min(1240, Math.Max(1000, area.Width - 80));
        Height = Math.Min(820, Math.Max(680, area.Height - 80));
        MinimumSize = new Size(1000, 680);

        _appSearch = Ui.SearchBox("Search apps", 240);
        _fileSearch = Ui.SearchBox("Search folders", 200);
        _leftSearch = Ui.SearchBox("Exact app name", 260);
        _hideSystem = Ui.Check("Hide Windows updates and built-in apps");
        _hideSystem.Checked = true;
        _includeUsb = Ui.Check("Include USB drives");

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildApps());
        tabs.TabPages.Add(BuildLeftovers());
        tabs.TabPages.Add(BuildJunk());
        tabs.TabPages.Add(BuildFiles());

        var status = new StatusStrip();
        status.Items.Add(_status);
        status.Items.Add(_progress);
        _status.Text = "Starting...";

        Controls.Add(tabs);
        Controls.Add(BuildHeader());
        Controls.Add(status);

        _appSearch.TextChanged += (_, _) => BindApps();
        _hideSystem.CheckedChanged += (_, _) => BindApps();
        _fileSearch.TextChanged += (_, _) => BindFileView();
        _fileFilter.SelectedIndexChanged += (_, _) => BindFileView();
        _appsGrid.SelectionChanged += (_, _) => ShowApp();
        _junkGrid.SelectionChanged += (_, _) => ShowJunk();
        _leftGrid.SelectionChanged += (_, _) => ShowLeftover();
        _fileGrid.SelectionChanged += (_, _) => ShowBucket();
        _browseGrid.SelectionChanged += (_, _) => ShowBrowse();
        _fileGrid.CellDoubleClick += (_, e) => OpenBucket(e.RowIndex);
        _browseGrid.CellDoubleClick += (_, e) => OpenBrowseEntry(e.RowIndex);
        _junkGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_junkGrid.IsCurrentCellDirty)
                _junkGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _junkGrid.CellContentClick += (_, e) => RejectUnsafeCheck(e.RowIndex, e.ColumnIndex);
        _leftSearch.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;
            e.SuppressKeyPress = true;
            await FindLeftovers();
        };

        new ToolTip().SetToolTip(_hideSystem, "Hides Windows updates, system components, built-in Store apps, and Store frameworks. Drivers stay visible.");
        Shown += async (_, _) => await RunBusy(async ct =>
        {
            await LoadApps(ct);
            await LoadJunk(ct);
        });
    }

    Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Ui.Header };
        var title = new Label
        {
            Text = "SafeUninstall",
            ForeColor = Color.White,
            Font = Ui.TitleFont,
            AutoSize = true,
            Location = new Point(20, 12)
        };
        var subtitle = new Label
        {
            Text = "Installed apps, leftover folders, and the files on this PC — with a keep or delete answer.",
            ForeColor = Color.FromArgb(191, 219, 254),
            AutoSize = false,
            Location = new Point(22, 50),
            Height = 24
        };
        var help = Ui.Secondary("How decisions work");
        help.Click += (_, _) => Confirm.ShowHelp(this);
        Control? admin = null;
        if (IsAdmin())
        {
            admin = new Label
            {
                Text = "Administrator",
                ForeColor = Color.White,
                AutoSize = true,
                Font = Ui.StrongFont
            };
        }
        else
        {
            var button = Ui.Secondary("Run as administrator");
            button.Click += (_, _) => RestartElevated();
            admin = button;
        }

        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        header.Controls.Add(help);
        header.Controls.Add(admin);
        header.Resize += (_, _) =>
        {
            admin.Top = 22;
            admin.Left = header.ClientSize.Width - admin.Width - 16;
            help.Top = 22;
            help.Left = admin.Left - help.Width - 8;
            subtitle.Width = Math.Max(200, help.Left - 36);
        };
        return header;
    }

    TabPage BuildApps()
    {
        var page = Page("Applications");
        var bar = Ui.Toolbar();
        var refresh = ActionButton(Ui.Secondary("Refresh"), bar);
        refresh.Click += async (_, _) => await RunBusy(LoadApps);
        _uninstallButton = ActionButton(Ui.Primary("Uninstall with official uninstaller"), bar);
        _uninstallButton.Click += async (_, _) => await UninstallSelected();
        var leftovers = ActionButton(Ui.Secondary("Find leftover folders"), bar);
        leftovers.Click += async (_, _) => await FindFromSelection();
        bar.Controls.Add(_hideSystem);
        bar.Controls.Add(_appSearch);
        Ui.StyleGrid(_appsGrid);
        _appsGrid.Columns.Add(Ui.Column("Name", "Name", 28, 160));
        _appsGrid.Columns.Add(Ui.Column("Publisher", "Publisher", 16, 100));
        _appsGrid.Columns.Add(Ui.Column("Version", "Version", 10, 80));
        var size = Ui.Column("Size", "Size", 10, 80);
        size.ValueType = typeof(long);
        _appsGrid.Columns.Add(size);
        _appsGrid.Columns.Add(Ui.Column("Installed", "Installed", 10, 90));
        _appsGrid.Columns.Add(Ui.Column("Safety", "Can I remove it?", 18, 150));
        page.Controls.Add(_appsGrid);
        page.Controls.Add(Ui.DetailHost(_appDetail));
        page.Controls.Add(bar);
        _appDetail.Text = "Programs from Windows and the Microsoft Store. \"Safe to uninstall\" opens that program's own uninstaller. Needed items stay protected.";
        return page;
    }

    TabPage BuildLeftovers()
    {
        var page = Page("Leftover folders");
        var bar = Ui.Toolbar();
        bar.Controls.Add(_leftSearch);
        var find = ActionButton(Ui.Primary("Find folders"), bar);
        find.Click += async (_, _) => await FindLeftovers();
        _deleteLeftButton = ActionButton(Ui.Danger("Delete leftover folder"), bar);
        _deleteLeftButton.Click += async (_, _) => await DeleteLeftover();
        Ui.StyleGrid(_leftGrid);
        _leftGrid.Columns.Add(Ui.Column("Folder", "Folder", 34, 220));
        var size = Ui.Column("Size", "Size", 10, 80);
        size.ValueType = typeof(long);
        _leftGrid.Columns.Add(size);
        var files = Ui.Column("Files", "Files", 8, 70);
        files.ValueType = typeof(long);
        _leftGrid.Columns.Add(files);
        _leftGrid.Columns.Add(Ui.Column("Advice", "Can I delete it?", 18, 160));
        _leftGrid.Columns.Add(Ui.Column("Why", "Why", 30, 180));
        page.Controls.Add(_leftGrid);
        page.Controls.Add(Ui.DetailHost(_leftDetail));
        page.Controls.Add(bar);
        _leftDetail.Text = "Finds a folder with the exact app name. Nothing is deleted while that app is still installed. You must type the folder name. Registry entries are never deleted.";
        return page;
    }

    TabPage BuildJunk()
    {
        var page = Page("Junk and caches");
        var bar = Ui.Toolbar();
        var refresh = ActionButton(Ui.Secondary("Refresh"), bar);
        refresh.Click += async (_, _) => await RunBusy(LoadJunk);
        var select = ActionButton(Ui.Secondary("Select all cleanable"), bar);
        select.Click += (_, _) => SelectCleanable();
        _deleteJunkButton = ActionButton(Ui.Danger("Delete selected"), bar);
        _deleteJunkButton.Click += async (_, _) => await DeleteJunk();
        Ui.StyleGrid(_junkGrid);
        _junkGrid.ReadOnly = false;
        _junkGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        var pick = new DataGridViewCheckBoxColumn
        {
            Name = "Pick",
            HeaderText = "",
            Width = 44,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ThreeState = false,
            TrueValue = true,
            FalseValue = false
        };
        _junkGrid.Columns.Add(pick);
        _junkGrid.Columns.Add(Ui.Column("Item", "Item", 18, 140));
        var size = Ui.Column("Size", "Size", 10, 80);
        size.ValueType = typeof(long);
        _junkGrid.Columns.Add(size);
        var files = Ui.Column("Files", "Files", 8, 70);
        files.ValueType = typeof(long);
        _junkGrid.Columns.Add(files);
        _junkGrid.Columns.Add(Ui.Column("Status", "Status", 12, 110));
        _junkGrid.Columns.Add(Ui.Column("What", "What it is", 32, 200));
        foreach (DataGridViewColumn column in _junkGrid.Columns)
            column.ReadOnly = column.Name != "Pick";
        page.Controls.Add(_junkGrid);
        page.Controls.Add(Ui.DetailHost(_junkDetail));
        page.Controls.Add(bar);
        _junkDetail.Text = "Only temp files, the Recycle Bin, shader caches, thumbnails, and browser caches. Cookies, passwords, documents, and Windows are not in this list.";
        return page;
    }

    TabPage BuildFiles()
    {
        var page = Page("All files");
        var bar = Ui.Toolbar();
        bar.Height = 96;
        bar.WrapContents = true;
        var scan = ActionButton(Ui.Primary("Scan all files on this PC"), bar);
        scan.Click += async (_, _) => await ScanFiles();
        _backButton = ActionButton(Ui.Secondary("Back"), bar);
        _backButton.Click += (_, _) => BrowseBack();
        var save = ActionButton(Ui.Secondary("Save report to Desktop"), bar);
        save.Click += (_, _) => SaveReport();
        bar.Controls.Add(_includeUsb);
        foreach (var label in new[]
        {
            "All",
            SafetyText.ForPath(SafetyLevel.Required),
            SafetyText.ForPath(SafetyLevel.Keep),
            SafetyText.ForPath(SafetyLevel.YourFiles),
            SafetyText.ForPath(SafetyLevel.Uninstallable),
            SafetyText.ForPath(SafetyLevel.Review),
            SafetyText.ForPath(SafetyLevel.SafeToDelete)
        })
            _fileFilter.Items.Add(label);
        _fileFilter.SelectedIndex = 0;
        bar.Controls.Add(_fileFilter);
        bar.Controls.Add(_fileSearch);

        _fileSummary.Dock = DockStyle.Top;
        _fileSummary.Height = 48;
        _fileSummary.Padding = new Padding(12, 6, 12, 0);
        _fileSummary.Text = "The scan groups every readable file on internal disks. It can take several minutes. Press Esc to stop. This page does not delete files.";

        Ui.StyleGrid(_fileGrid);
        Ui.StyleGrid(_browseGrid);
        AddFileColumns(_fileGrid);
        AddFileColumns(_browseGrid);
        _browseGrid.Visible = false;
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(_browseGrid);
        host.Controls.Add(_fileGrid);
        page.Controls.Add(host);
        page.Controls.Add(Ui.DetailHost(_fileDetail));
        page.Controls.Add(_fileSummary);
        page.Controls.Add(bar);
        _fileDetail.Text = "Double-click a folder to look inside. Advice is for the whole folder. Safe cache is removed from Junk and caches, not by deleting the parent folder.";
        return page;
    }

    static void AddFileColumns(DataGridView grid)
    {
        grid.Columns.Add(Ui.Column("Location", "Location", 32, 220));
        var size = Ui.Column("Size", "Size", 10, 80);
        size.ValueType = typeof(long);
        grid.Columns.Add(size);
        var files = Ui.Column("Files", "Files", 8, 70);
        files.ValueType = typeof(long);
        grid.Columns.Add(files);
        grid.Columns.Add(Ui.Column("Advice", "If you delete it", 16, 160));
        grid.Columns.Add(Ui.Column("Why", "Why", 28, 180));
    }

    static TabPage Page(string title) => new(title) { BackColor = Ui.Page, Padding = new Padding(0) };

    Button ActionButton(Button button, FlowLayoutPanel bar)
    {
        _actions.Add(button);
        bar.Controls.Add(button);
        return button;
    }

    async Task RunBusy(Func<CancellationToken, Task> work)
    {
        if (_busy)
        {
            SetStatus("Wait for the current task to finish, or press Esc to cancel.");
            return;
        }

        _busy = true;
        _cts = new CancellationTokenSource();
        ToggleBusy(true);
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            if (!IsDisposed)
                MessageBox.Show(this, ex.Message, "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
                ToggleBusy(false);
        }
    }

    void ToggleBusy(bool busy)
    {
        _progress.Visible = busy;
        foreach (var button in _actions)
            button.Enabled = !busy;
        if (!busy)
            UpdateButtons();
    }

    async Task LoadApps(CancellationToken ct)
    {
        SetStatus("Reading installed programs...");
        var progress = new Progress<string>(SetStatus);
        var apps = await Task.Run(() => InstalledAppScanner.Scan(ct, progress, includeStore: true), ct);
        if (IsDisposed)
            return;
        _apps = apps;
        BindApps();
        var shown = _appsGrid.Rows.Count;
        SetStatus($"{shown:N0} programs shown. {_apps.Count:N0} found, including hidden Windows updates and built-in apps.");
    }

    async Task LoadJunk(CancellationToken ct)
    {
        SetStatus("Measuring caches...");
        IProgress<string> progress = new Progress<string>(SetStatus);
        var junk = await Task.Run(() =>
        {
            var list = JunkCatalog.Create();
            foreach (var category in list)
            {
                ct.ThrowIfCancellationRequested();
                progress.Report("Measuring " + category.Name + "...");
                JunkCatalog.Measure(category, ct);
            }

            return list;
        }, ct);
        if (IsDisposed)
            return;
        _junk = junk;
        BindJunk();
        var bytes = junk.Where(category => category.CanClean).Sum(category => Math.Max(category.Bytes, 0L));
        SetStatus("Cleanable temp and cache: " + SizeText.Format(bytes) + ".");
    }

    void BindApps()
    {
        var text = _appSearch.Text.Trim();
        var rows = _apps.Where(app =>
        {
            if (_hideSystem.Checked && app.HideByDefault)
                return false;
            if (text.Length == 0)
                return true;
            return app.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || app.Publisher.Contains(text, StringComparison.OrdinalIgnoreCase);
        });

        _appsGrid.Rows.Clear();
        foreach (var app in rows)
        {
            var index = _appsGrid.Rows.Add(
                app.DisplayName,
                app.Publisher,
                app.Version,
                app.SizeBytes,
                string.IsNullOrWhiteSpace(app.InstallDate) ? "Unknown" : app.InstallDate,
                SafetyText.ForApp(app.Safety));
            _appsGrid.Rows[index].Tag = app;
        }

        ShowApp();
        UpdateButtons();
    }

    void BindJunk()
    {
        _junkGrid.Rows.Clear();
        foreach (var category in _junk)
        {
            var index = _junkGrid.Rows.Add(false, category.Name, category.Bytes, category.FileCount, JunkStatus(category), category.Reason);
            _junkGrid.Rows[index].Tag = category;
        }

        ShowJunk();
        UpdateButtons();
    }

    static string JunkStatus(JunkCategory category)
    {
        if (!category.Measured)
            return "Measuring...";
        if (category.AccessDenied)
            return "Needs administrator";
        if (category.IsRecycleBin && category.Bytes < 0)
            return "Ready";
        if (category.Bytes == 0 || category.FileCount == 0)
            return "Empty";
        return category.Partial ? "Partly measured" : "Ready";
    }

    void ShowApp()
    {
        if (_appsGrid.CurrentRow?.Tag is not InstalledApp app)
        {
            UpdateButtons();
            return;
        }

        var plan = UninstallService.Plan(app);
        _appDetail.Text =
            app.DisplayName + "\r\n"
            + SafetyText.ForApp(app.Safety) + ". " + app.Reason + "\r\n"
            + "Publisher: " + Blank(app.Publisher) + "    Version: " + Blank(app.Version) + "    Size: " + SizeText.Format(app.SizeBytes) + "\r\n"
            + "Source: " + (app.IsStore ? "Microsoft Store" : "Desktop installer") + "\r\n"
            + "Location: " + Blank(app.InstallLocation, "Install location was not recorded.") + "\r\n"
            + (plan.CanRun ? "Uninstaller: " + plan.DisplayCommand : "Uninstall: " + (app.UninstallBlock.Length > 0 ? app.UninstallBlock : plan.Message));
        UpdateButtons();
    }

    void ShowJunk()
    {
        if (_junkGrid.CurrentRow?.Tag is not JunkCategory category)
            return;
        _junkDetail.Text = category.Name + "\r\n" + category.Reason + "\r\n"
            + (category.IsRecycleBin ? "Recycle Bin" : category.Path) + "\r\n"
            + "Size: " + SizeText.Format(category.Bytes) + "    Files: " + (category.FileCount < 0 ? "Unknown" : category.FileCount.ToString("N0"))
            + "\r\nSafeUninstall only empties this item. It does not delete Windows, documents, or installed programs.";
    }

    async Task UninstallSelected()
    {
        if (_appsGrid.CurrentRow?.Tag is not InstalledApp app)
            return;
        if (!app.CanUninstall)
        {
            _appDetail.Text = app.UninstallBlock.Length > 0 ? app.UninstallBlock : app.Reason;
            return;
        }

        if (app.IsStore)
        {
            if (!Confirm.Store(this, app.DisplayName))
                return;
            await RunBusy(async ct =>
            {
                var error = await UninstallService.RemoveStoreAppAsync(app.PackageFullName, ct);
                if (error != null)
                    throw new InvalidOperationException(error.Trim());
                SetStatus(app.DisplayName + " was removed for your Windows user.");
                await LoadApps(ct);
            });
            return;
        }

        var plan = UninstallService.Plan(app);
        if (!plan.CanRun)
        {
            _appDetail.Text = plan.Message;
            return;
        }

        var warning = app.Safety switch
        {
            SafetyLevel.Keep => "Other programs may stop working if you remove this.\r\n\r\n",
            SafetyLevel.Review => "Check this before you continue.\r\n\r\n",
            _ => ""
        };
        var body = warning + SafetyText.ForApp(app.Safety) + "\r\n" + app.Reason + "\r\n\r\n"
            + app.DisplayName + "\r\nPublisher: " + Blank(app.Publisher) + "\r\nVersion: " + Blank(app.Version)
            + "\r\n\r\nSafeUninstall will open this official uninstaller. It does not delete the folder itself:\r\n"
            + plan.DisplayCommand;
        if (!Confirm.Uninstall(this, body))
            return;

        try
        {
            UninstallService.Launch(plan);
            SetStatus("Official uninstaller opened. Click Refresh when it finishes, then Find leftover folders if you want to look for leftovers.");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            MessageBox.Show(this, "The uninstaller was not started.\r\n\r\n" + ex.Message, "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    async Task FindFromSelection()
    {
        if (_appsGrid.CurrentRow?.Tag is not InstalledApp app)
        {
            _appDetail.Text = "Select a program first.";
            return;
        }

        _leftSearch.Text = app.DisplayName;
        if (Parent is TabControl == false)
        {
            var tabs = _leftSearch.FindForm()?.Controls.OfType<TabControl>().FirstOrDefault();
            if (tabs != null)
                tabs.SelectedIndex = 1;
        }

        var tabs2 = Controls.OfType<TabControl>().FirstOrDefault();
        if (tabs2 != null)
            tabs2.SelectedIndex = 1;
        await FindLeftovers();
    }

    async Task FindLeftovers()
    {
        var name = _leftSearch.Text.Trim();
        if (name.Length == 0)
        {
            _leftDetail.Text = "Type the exact app name, or select an app and click Find leftover folders.";
            return;
        }

        await RunBusy(async ct =>
        {
            var progress = new Progress<string>(SetStatus);
            var apps = _apps;
            var found = await Task.Run(() =>
            {
                var hits = LeftoverScanner.Find(name, apps, ct, progress, out var message);
                return (Hits: hits, Message: message);
            }, ct);
            if (IsDisposed)
                return;
            BindLeftovers(found.Hits);
            _leftDetail.Text = found.Message.Length > 0 ? found.Message : found.Hits.Count + " folder(s) matched \"" + name + "\".";
            SetStatus(_leftDetail.Text);
        });
    }

    void BindLeftovers(List<LeftoverHit> hits)
    {
        _leftGrid.Rows.Clear();
        foreach (var hit in hits.OrderByDescending(hit => hit.Bytes))
        {
            var why = hit.BlockReason ?? "You can delete this leftover after you type the folder name. It may include settings and saved passwords for that app.";
            var index = _leftGrid.Rows.Add(hit.Path, hit.Bytes, hit.Files, hit.Advice, why);
            _leftGrid.Rows[index].Tag = hit;
        }

        ShowLeftover();
        UpdateButtons();
    }

    void ShowLeftover()
    {
        if (_leftGrid.CurrentRow?.Tag is not LeftoverHit hit)
        {
            UpdateButtons();
            return;
        }

        _leftDetail.Text = hit.Path + "\r\n" + hit.Advice + "\r\n"
            + "Size: " + SizeText.Format(hit.Bytes) + "    Files: " + (hit.Files < 0 ? "Unknown" : hit.Files.ToString("N0")) + "\r\n"
            + (hit.BlockReason ?? "You can delete this after you type the folder name. Registry entries are not deleted.");
        UpdateButtons();
    }

    async Task DeleteLeftover()
    {
        if (_leftGrid.CurrentRow?.Tag is not LeftoverHit hit)
            return;
        var inUse = _apps.Any(app => NamesMatch(app, hit.FolderName));
        var block = LeftoverScanner.DeleteBlock(hit.Path, inUse);
        if (block != null)
        {
            _leftDetail.Text = block;
            return;
        }

        if (!Confirm.Leftover(this, hit))
            return;

        await RunBusy(async ct =>
        {
            var report = await Task.Run(() =>
            {
                var again = LeftoverScanner.DeleteBlock(hit.Path, _apps.Any(app => NamesMatch(app, hit.FolderName)));
                if (again != null)
                    throw new InvalidOperationException(again);
                if (!string.Equals(hit.FolderName, Path.GetFileName(PathRules.TrimEnd(hit.Path)), StringComparison.Ordinal))
                    throw new InvalidOperationException("The folder name changed, so it was not deleted.");
                return SafeDelete.DeleteTree(hit.Path, DeleteKind.Leftover, deleteRoot: true, ct);
            }, ct);
            if (IsDisposed)
                return;
            var extra = report.Skipped > 0
                ? "\r\n" + report.Skipped.ToString("N0") + " item(s) were locked or were links, so they were left in place."
                : "";
            MessageBox.Show(this, "Removed " + SizeText.Format(report.DeletedBytes) + " in " + report.DeletedFiles.ToString("N0") + " files." + extra, "Leftover folder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    static bool NamesMatch(InstalledApp app, string folderName)
    {
        var name = LeftoverScanner.Normalize(folderName);
        if (LeftoverScanner.Normalize(app.DisplayName) == name)
            return true;
        if (string.IsNullOrWhiteSpace(app.InstallLocation))
            return false;
        return LeftoverScanner.Normalize(Path.GetFileName(PathRules.TrimEnd(app.InstallLocation))) == name;
    }

    void SelectCleanable()
    {
        _junkGrid.EndEdit();
        foreach (DataGridViewRow row in _junkGrid.Rows)
        {
            if (row.Tag is JunkCategory category && category.CanClean)
                row.Cells["Pick"].Value = true;
        }
    }

    async Task DeleteJunk()
    {
        _junkGrid.EndEdit();
        var picked = new List<JunkCategory>();
        foreach (DataGridViewRow row in _junkGrid.Rows)
        {
            if (row.Cells["Pick"].Value is true && row.Tag is JunkCategory category && category.CanClean)
                picked.Add(category);
        }

        if (picked.Count == 0)
        {
            _junkDetail.Text = "Select one or more items that have a size. Empty items are already clean.";
            return;
        }

        if (!Confirm.Junk(this, picked))
            return;

        await RunBusy(async ct =>
        {
            var report = await Task.Run(() => CleanJunk(picked, ct), ct);
            if (IsDisposed)
                return;
            await LoadJunk(ct);
            var skipped = report.Skipped > 0 ? "\r\nSkipped " + report.Skipped.ToString("N0") + " files that were in use or could not be read." : "";
            var errors = report.Errors.Count > 0 ? "\r\n" + string.Join("\r\n", report.Errors) : "";
            MessageBox.Show(this,
                "Removed " + SizeText.Format(report.DeletedBytes) + " in " + report.DeletedFiles.ToString("N0") + " files."
                + (report.EmptiedRecycleBin ? "\r\nRecycle Bin emptied." : "")
                + skipped + errors,
                "Cleanup finished",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
    }

    static DeleteReport CleanJunk(IReadOnlyList<JunkCategory> items, CancellationToken ct)
    {
        var total = new DeleteReport();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (!item.CanClean)
                continue;
            if (item.IsRecycleBin)
            {
                var hr = RecycleBin.Empty();
                if (RecycleBin.Succeeded(hr))
                    total.EmptiedRecycleBin = true;
                else if (total.Errors.Count < 8)
                    total.Errors.Add("Recycle Bin could not be emptied.");
                continue;
            }

            var report = SafeDelete.DeleteTree(item.Path, DeleteKind.Junk, deleteRoot: false, ct, item.Patterns, item.IncludeSubdirectories);
            total.DeletedFiles += report.DeletedFiles;
            total.DeletedBytes += report.DeletedBytes;
            total.Skipped += report.Skipped;
            foreach (var error in report.Errors)
            {
                if (total.Errors.Count < 8)
                    total.Errors.Add(error);
            }
        }

        return total;
    }

    void RejectUnsafeCheck(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || _junkGrid.Columns[columnIndex].Name != "Pick")
            return;
        if (_junkGrid.Rows[rowIndex].Tag is JunkCategory category && !category.CanClean)
        {
            _junkGrid.Rows[rowIndex].Cells["Pick"].Value = false;
            _junkDetail.Text = category.AccessDenied
                ? "This folder could not be read. Use Run as administrator, then click Refresh."
                : "There is nothing to remove here.";
        }
    }

    async Task ScanFiles()
    {
        await RunBusy(async ct =>
        {
            SetStatus("Scanning internal disks. This can take several minutes. Press Esc to stop. Nothing is deleted.");
            var usb = _includeUsb.Checked;
            var progress = new Progress<string>(SetStatus);
            var result = await Task.Run(() => FileSafetyScanner.Scan(usb, ct, progress), ct);
            if (IsDisposed)
                return;
            _fileScan = result;
            _browseStack.Clear();
            _browseGrid.Visible = false;
            _fileGrid.Visible = true;
            BindFileView();
            var prefix = result.Cancelled ? "Scan stopped. Showing what was read so far. " : "";
            SetStatus(prefix + $"{result.TotalFiles:N0} files, {SizeText.Format(result.TotalBytes)}. {string.Join("   ", result.DriveLines)}. Could not read {result.UnreadableDirectories:N0} folders. Skipped {result.SkippedLinks:N0} links.");
        });
    }

    void BindFileView()
    {
        if (_browseStack.Count > 0)
            BindBrowse();
        else
            BindOverview();
    }

    void BindOverview()
    {
        _fileGrid.Rows.Clear();
        if (_fileScan == null)
        {
            _fileSummary.Text = "The scan groups every readable file on internal disks. It can take several minutes. Press Esc to stop. This page does not delete files.";
            return;
        }

        _fileSummary.Text = _fileScan.Summary();
        foreach (var bucket in FilteredBuckets())
        {
            var index = _fileGrid.Rows.Add(bucket.Path, bucket.Bytes, bucket.Files, SafetyText.ForPath(bucket.Advice), bucket.DisplayReason());
            _fileGrid.Rows[index].Tag = bucket;
        }

        ShowBucket();
    }

    IEnumerable<FolderBucket> FilteredBuckets()
    {
        if (_fileScan == null)
            yield break;
        var text = _fileSearch.Text.Trim();
        var level = SelectedFileLevel();
        foreach (var bucket in _fileScan.Buckets)
        {
            if (level.HasValue && bucket.Advice != level.Value)
                continue;
            if (text.Length > 0
                && !bucket.Path.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !bucket.DisplayReason().Contains(text, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return bucket;
        }
    }

    void BindBrowse()
    {
        _browseGrid.Rows.Clear();
        var text = _fileSearch.Text.Trim();
        var level = SelectedFileLevel();
        var shown = 0;
        foreach (var entry in _browse)
        {
            if (level.HasValue && entry.Level != level.Value)
                continue;
            if (text.Length > 0
                && !entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                && !entry.Reason.Contains(text, StringComparison.OrdinalIgnoreCase))
                continue;
            var files = entry.IsDirectory ? -1L : 1L;
            var index = _browseGrid.Rows.Add(entry.FullPath, entry.Bytes, files, SafetyText.ForPath(entry.Level), entry.Reason);
            _browseGrid.Rows[index].Tag = entry;
            shown++;
        }

        _fileSummary.Text = _browseStack.Count == 0
            ? ""
            : _browseStack.Peek() + ( _browse.Count >= 8000 ? "    Showing the first 8,000 items." : "    " + shown.ToString("N0") + " shown.");
        ShowBrowse();
    }

    SafetyLevel? SelectedFileLevel()
    {
        return _fileFilter.SelectedIndex switch
        {
            1 => SafetyLevel.Required,
            2 => SafetyLevel.Keep,
            3 => SafetyLevel.YourFiles,
            4 => SafetyLevel.Uninstallable,
            5 => SafetyLevel.Review,
            6 => SafetyLevel.SafeToDelete,
            _ => null
        };
    }

    void ShowBucket()
    {
        if (_fileGrid.CurrentRow?.Tag is not FolderBucket bucket)
            return;
        _fileDetail.Text = bucket.Path + "\r\n" + SafetyText.ForPath(bucket.Advice) + "\r\n" + bucket.DisplayReason()
            + "\r\nSize: " + SizeText.Format(bucket.Bytes) + "    Files: " + (bucket.Files < 0 ? "Unknown" : bucket.Files.ToString("N0"))
            + "\r\nDouble-click to look inside. This page does not delete files.";
    }

    void ShowBrowse()
    {
        if (_browseGrid.CurrentRow?.Tag is not BrowseEntry entry)
            return;
        _fileDetail.Text = entry.FullPath + "\r\n" + SafetyText.ForPath(entry.Level) + "\r\n" + entry.Reason
            + (entry.IsDirectory ? "\r\nDouble-click to open this folder." : "\r\nFiles are not opened from here.");
    }

    void OpenBucket(int rowIndex)
    {
        if (rowIndex < 0 || _fileGrid.Rows[rowIndex].Tag is not FolderBucket bucket)
            return;
        if (!Directory.Exists(bucket.Path))
        {
            _fileDetail.Text = "That folder is no longer there.";
            return;
        }

        _ = ShowFolder(bucket.Path, fresh: true);
    }

    void OpenBrowseEntry(int rowIndex)
    {
        if (rowIndex < 0 || _browseGrid.Rows[rowIndex].Tag is not BrowseEntry entry)
            return;
        if (entry.BlockedLink || !entry.IsDirectory)
        {
            ShowBrowse();
            return;
        }

        _ = ShowFolder(entry.FullPath, fresh: false);
    }

    async Task ShowFolder(string path, bool fresh)
    {
        await RunBusy(async ct =>
        {
            SetStatus("Reading " + path);
            var entries = await Task.Run(() => FileSafetyScanner.ListLevel(path, ct), ct);
            if (IsDisposed)
                return;
            if (fresh)
                _browseStack.Clear();
            _browseStack.Push(path);
            _browse = entries;
            _fileGrid.Visible = false;
            _browseGrid.Visible = true;
            BindBrowse();
            SetStatus(path);
        });
    }

    void BrowseBack()
    {
        if (_browseStack.Count == 0)
            return;
        _browseStack.Pop();
        if (_browseStack.Count == 0)
        {
            _browseGrid.Visible = false;
            _fileGrid.Visible = true;
            BindOverview();
            return;
        }

        _ = ShowFolder(_browseStack.Pop(), fresh: false);
    }

    void SaveReport()
    {
        if (_fileScan == null || _fileScan.Buckets.Count == 0)
        {
            MessageBox.Show(this, "Scan the PC first. The report is the grouped file list.", "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save file report",
            Filter = "CSV report (*.csv)|*.csv",
            FileName = "SafeUninstall File Report.csv",
            DefaultExt = "csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var path = dialog.FileName;
        try
        {
            using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            writer.WriteLine("Path,Size bytes,Files,Advice,Why");
            foreach (var bucket in _fileScan.Buckets)
            {
                writer.WriteLine(string.Join(",",
                    Csv(bucket.Path),
                    bucket.Bytes.ToString(),
                    bucket.Files.ToString(),
                    Csv(SafetyText.ForPath(bucket.Advice)),
                    Csv(bucket.DisplayReason())));
            }

            SetStatus("Saved " + path);
            MessageBox.Show(this, "Saved the grouped file report.\r\n\r\n" + path, "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The report was not saved.\r\n\r\n" + ex.Message, "SafeUninstall", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    static string Csv(string value) => CsvReport.Cell(value);

    void UpdateButtons()
    {
        if (_busy)
            return;
        if (_uninstallButton != null)
            _uninstallButton.Enabled = _appsGrid.CurrentRow?.Tag is InstalledApp app && app.CanUninstall;
        if (_deleteLeftButton != null)
            _deleteLeftButton.Enabled = _leftGrid.CurrentRow?.Tag is LeftoverHit hit && hit.BlockReason == null;
        if (_deleteJunkButton != null)
            _deleteJunkButton.Enabled = true;
        if (_backButton != null)
            _backButton.Enabled = _browseStack.Count > 0;
    }

    void RestartElevated()
    {
        var path = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("This copy of SafeUninstall could not be started as administrator.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "runas"
            });
            Close();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            SetStatus("Administrator mode was not started.");
        }
    }

    static bool IsAdmin()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    static string Blank(string value, string fallback = "Unknown") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    void SetStatus(string text)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(text));
            return;
        }

        _status.Text = text;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && _cts != null)
        {
            _cts.Cancel();
            return true;
        }

        if (keyData == Keys.F5 && !_busy)
        {
            var tabs = Controls.OfType<TabControl>().FirstOrDefault();
            var index = tabs?.SelectedIndex ?? 0;
            if (index == 0)
                _ = RunBusy(LoadApps);
            else if (index == 1)
                _ = FindLeftovers();
            else if (index == 2)
                _ = RunBusy(LoadJunk);
            else
                _ = ScanFiles();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            _cts?.Cancel();
            e.Cancel = true;
            SetStatus("Stopping the current task. Close again in a moment.");
        }

        base.OnFormClosing(e);
    }
}
