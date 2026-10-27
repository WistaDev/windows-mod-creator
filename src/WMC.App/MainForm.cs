using System.Diagnostics;
using WMC.Core;

namespace WMC.App;

public sealed class MainForm : Form
{
    private WmcProject _project = new();
    private bool _busy;
    private List<WimEdition> _editions = new();
    private readonly IProgress<string> _progress;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _pgProject = new("Project");
    private readonly TabPage _pgEdition = new("Edition");
    private readonly TabPage _pgApps = new("Apps");
    private readonly TabPage _pgBuild = new("Build");

    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Bottom,
        Height = 160,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Courier New", 9f),
        BackColor = SystemColors.Window,
        ForeColor = SystemColors.WindowText,
        BorderStyle = BorderStyle.Fixed3D,
    };

    private readonly Label _status = new()
    {
        Dock = DockStyle.Bottom,
        Height = 20,
        BorderStyle = BorderStyle.Fixed3D,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    // Project tab
    private readonly TextBox _txtIso = new();
    private readonly TextBox _txtWork = new();
    private readonly TextBox _txtLabel = new() { Text = "WMC_WINDOWS" };
    private readonly Button _btnExtract = Btn("Load ISO", 110);
    private readonly Button _btnOpen = Btn("Open project...", 110);

    // Edition tab
    private readonly ListView _lvEditions = NewListView(("Index", 60), ("Edition", 520));
    private readonly Button _btnMount = Btn("Mount selected", 120);
    private readonly Button _btnUnmountCommit = Btn("Commit and unmount", 130);
    private readonly Button _btnUnmountDiscard = Btn("Discard and unmount", 130);

    // Apps tab
    private readonly TextBox _txtAppSrc = new() { ReadOnly = true };
    private readonly ComboBox _cmbExe = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtAppName = new();
    private readonly TextBox _txtAppPub = new();
    private readonly TextBox _txtAppVer = new() { Text = "1.0" };
    private readonly TextBox _txtAppIcon = new();
    private readonly TextBox _txtAppUninst = new();
    private readonly TextBox _txtAppModify = new();
    private readonly CheckBox _chkStart = new() { Text = "Start Menu shortcut", Checked = true, AutoSize = true };
    private readonly CheckBox _chkDesk = new() { Text = "Desktop shortcut", AutoSize = true };
    private readonly Button _btnPublish = Btn("Publish", 90);
    private readonly Label _lblAppsHint = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly ListView _lvApps = NewListView(("Name", 160), ("Version", 70), ("Publisher", 140), ("Uninstaller", 80), ("Installed to", 300));

    // Build tab
    private readonly TextBox _txtOutIso = new();
    private readonly Button _btnBuild = Btn("Build ISO", 100);

    public MainForm()
    {
        Text = "WMC - Windows Mod Creator";
        Font = new Font("Tahoma", 8.25f);
        BackColor = SystemColors.Control;
        ClientSize = new Size(860, 680);
        MinimumSize = new Size(720, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _progress = new Progress<string>(Log);

        BuildProjectTab();
        BuildEditionTab();
        BuildAppsTab();
        BuildBuildTab();

        _tabs.TabPages.Add(_pgProject);

        // Fill control first, then the Bottom ones (WinForms docking order).
        Controls.Add(_tabs);
        Controls.Add(_log);
        Controls.Add(_status);

        FormClosing += OnFormClosing;
        UpdateState();
        Log("Ready. Choose a Windows ISO in the Project tab and click Load ISO.");
    }

    // ------------------------------------------------------------------ UI construction

    private static Button Btn(string text, int width = 75) => new()
    {
        Text = text,
        Width = width,
        Height = 23,
        FlatStyle = FlatStyle.Standard,
    };

    private static ListView NewListView(params (string Header, int Width)[] columns)
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            GridLines = true,
        };
        foreach (var (h, w) in columns) lv.Columns.Add(h, w);
        return lv;
    }

    private static TableLayoutPanel NewTable()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8) };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return t;
    }

    private static void AddRow(TableLayoutPanel t, string label, Control ctl, Button? btn = null)
    {
        int r = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) }, 0, r);
        ctl.Dock = DockStyle.Fill;
        ctl.Margin = new Padding(0, 3, 0, 3);
        t.Controls.Add(ctl, 1, r);
        if (btn is not null)
        {
            btn.Margin = new Padding(6, 2, 0, 2);
            t.Controls.Add(btn, 2, r);
        }
    }

    private static void AddWide(TableLayoutPanel t, Control ctl)
    {
        int r = t.RowCount++;
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(ctl, 0, r);
        t.SetColumnSpan(ctl, 3);
    }

    private static GroupBox NewGroup(string title, Control content)
    {
        var g = new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(6, 2, 6, 6),
            Margin = new Padding(0, 0, 0, 6),
        };
        g.Controls.Add(content);
        return g;
    }

    private void BuildProjectTab()
    {
        var t = NewTable();

        var btnIso = Btn("Browse...");
        btnIso.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Windows ISO (*.iso)|*.iso|All files (*.*)|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) _txtIso.Text = d.FileName;
        };

        var btnWork = Btn("Browse...");
        btnWork.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Working folder for this project (needs about 15 GB of free space)" };
            if (d.ShowDialog(this) == DialogResult.OK) _txtWork.Text = d.SelectedPath;
        };

        AddRow(t, "Windows ISO:", _txtIso, btnIso);
        AddRow(t, "Working folder:", _txtWork, btnWork);
        AddRow(t, "Volume label:", _txtLabel);

        var flow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        flow.Controls.Add(_btnExtract);
        flow.Controls.Add(_btnOpen);
        AddWide(t, flow);

        _btnExtract.Click += OnExtract;
        _btnOpen.Click += OnOpenProject;

        _pgProject.Controls.Add(t);
    }

    private void BuildEditionTab()
    {
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        buttons.Controls.Add(_btnMount);
        buttons.Controls.Add(_btnUnmountCommit);
        buttons.Controls.Add(_btnUnmountDiscard);

        _btnMount.Click += OnMount;
        _btnUnmountCommit.Click += (_, _) => OnUnmount(commit: true);
        _btnUnmountDiscard.Click += (_, _) => OnUnmount(commit: false);
        _lvEditions.SelectedIndexChanged += (_, _) => UpdateState();

        _pgEdition.Controls.Add(_lvEditions);
        _pgEdition.Controls.Add(buttons);
    }

    private void BuildAppsTab()
    {
        // Group 1: files
        var files = NewTable();
        var btnSrc = Btn("Browse...");
        btnSrc.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Application folder (the files exactly as they should appear in Program Files)" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _txtAppSrc.Text = d.SelectedPath;
            FillExeList(d.SelectedPath);
        };
        _cmbExe.SelectedIndexChanged += (_, _) => AutofillFromExe();
        AddRow(files, "Application folder:", _txtAppSrc, btnSrc);
        AddRow(files, "Main executable:", _cmbExe);

        // Group 2: Programs and Features entry
        var entry = NewTable();
        var btnIcon = Btn("Browse...");
        btnIcon.Click += (_, _) => PickFile(_txtAppIcon, "Select icon", "Icons and programs (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All files (*.*)|*.*");
        var btnUninst = Btn("Browse...");
        btnUninst.Click += (_, _) => PickFile(_txtAppUninst, "Select uninstaller", "Programs (*.exe;*.msi;*.bat;*.cmd)|*.exe;*.msi;*.bat;*.cmd|All files (*.*)|*.*");
        var btnModify = Btn("Browse...");
        btnModify.Click += (_, _) => PickFile(_txtAppModify, "Select modify/repair program", "Programs (*.exe;*.msi;*.bat;*.cmd)|*.exe;*.msi;*.bat;*.cmd|All files (*.*)|*.*");

        AddRow(entry, "Name:", _txtAppName);
        AddRow(entry, "Publisher:", _txtAppPub);
        AddRow(entry, "Icon:", _txtAppIcon, btnIcon);
        AddRow(entry, "Uninstaller path:", _txtAppUninst, btnUninst);
        AddRow(entry, "Version:", _txtAppVer);
        AddRow(entry, "Modify path:", _txtAppModify, btnModify);

        var checks = new FlowLayoutPanel { AutoSize = true };
        checks.Controls.Add(_chkStart);
        checks.Controls.Add(_chkDesk);
        AddWide(entry, checks);

        // Publish row
        var publish = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
        publish.Controls.Add(_btnPublish);
        publish.Controls.Add(_lblAppsHint);
        _btnPublish.Click += OnPublish;

        var listGroup = new GroupBox { Text = "Published apps", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6) };
        listGroup.Controls.Add(_lvApps);

        _pgApps.Padding = new Padding(6);
        _pgApps.Controls.Add(listGroup);                                   // Fill first
        _pgApps.Controls.Add(publish);
        _pgApps.Controls.Add(NewGroup("Programs and Features entry", entry));
        _pgApps.Controls.Add(NewGroup("Application files", files));        // last added = top-most
    }

    private void BuildBuildTab()
    {
        var t = NewTable();

        var btnOut = Btn("Browse...");
        btnOut.Click += (_, _) =>
        {
            using var d = new SaveFileDialog { Filter = "ISO image (*.iso)|*.iso", FileName = "WMC.iso" };
            if (d.ShowDialog(this) == DialogResult.OK) _txtOutIso.Text = d.FileName;
        };

        AddRow(t, "Output ISO:", _txtOutIso, btnOut);
        _btnBuild.Margin = new Padding(0, 10, 0, 0);
        AddWide(t, _btnBuild);
        _btnBuild.Click += OnBuild;

        _pgBuild.Controls.Add(t);
    }

    // ------------------------------------------------------------------ Handlers

    private async void OnExtract(object? sender, EventArgs e)
    {
        string iso = _txtIso.Text.Trim();
        string work = _txtWork.Text.Trim();
        string label = _txtLabel.Text.Trim();

        if (!File.Exists(iso)) { Warn("Select a valid ISO file."); return; }
        if (work.Length == 0) { Warn("Choose a working folder."); return; }

        work = Path.GetFullPath(work);
        if (Directory.Exists(work) && Directory.EnumerateFileSystemEntries(work).Any()
            && !Confirm("The working folder is not empty. Continue anyway?"))
            return;

        var proj = new WmcProject { WorkDir = work, IsoPath = iso, VolumeLabel = label.Length > 0 ? label : "WMC_WINDOWS" };
        List<WimEdition>? editions = null;

        await RunBusy(async p =>
        {
            await IsoService.ExtractAsync(iso, proj.IsoDir, p);
            proj.Extracted = true;
            proj.Save();
            editions = await ReadEditionsAsync(proj, p);
        }, onDone: () =>
        {
            _project = proj;
            _editions = editions ?? new();
            BindEditions();
            _txtOutIso.Text = Path.Combine(proj.WorkDir, "WMC.iso");
            RefreshApps();
            SyncTabs();
            _tabs.SelectedTab = _pgEdition;
        });
    }

    private async void OnOpenProject(object? sender, EventArgs e)
    {
        using var d = new FolderBrowserDialog { Description = "Working folder of an existing WMC project (contains wmc-project.json)" };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        var proj = WmcProject.Load(d.SelectedPath);
        if (proj is null) { Warn("No wmc-project.json found in that folder."); return; }

        List<WimEdition>? editions = null;
        await RunBusy(async p =>
        {
            if (proj.Extracted && !proj.Mounted) editions = await ReadEditionsAsync(proj, p);
        }, onDone: () =>
        {
            _project = proj;
            _editions = editions ?? new();
            _txtIso.Text = proj.IsoPath;
            _txtWork.Text = proj.WorkDir;
            _txtLabel.Text = proj.VolumeLabel;
            _txtOutIso.Text = Path.Combine(proj.WorkDir, "WMC.iso");
            BindEditions();
            RefreshApps();
            SyncTabs();
            Log($"Project opened: {proj.WorkDir}");
            if (proj.Mounted)
                Log("Note: this project is marked as mounted. If Windows was restarted since, run 'dism /Cleanup-Wim' and use Discard and unmount.");
        });
    }

    private async void OnMount(object? sender, EventArgs e)
    {
        if (_lvEditions.SelectedItems.Count == 0) { Warn("Select an edition."); return; }
        int index = (int)_lvEditions.SelectedItems[0].Tag!;

        await RunBusy(p => WimService.MountAsync(_project, index, p), onDone: () =>
        {
            Log("Image mounted at: " + _project.MountDir);
            _tabs.SelectedTab = _pgApps;
        });
    }

    private async void OnUnmount(bool commit)
    {
        if (!_project.Mounted) return;
        if (!commit && !Confirm("Discard all changes made to the image?")) return;
        await RunBusy(p => WimService.UnmountAsync(_project, commit, p));
    }

    private async void OnPublish(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }
        if (!Directory.Exists(_txtAppSrc.Text)) { Warn("Choose the application folder."); return; }
        if (_cmbExe.SelectedItem is not string exe) { Warn("Choose the main executable."); return; }

        string name = _txtAppName.Text.Trim();
        if (name.Length == 0) { Warn("Enter the application name."); return; }

        if (_project.Apps.Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            && !Confirm($"'{name}' was already published. Overwrite its files and registry entries?"))
            return;

        var app = new AppInfo
        {
            Name = name,
            Publisher = _txtAppPub.Text.Trim(),
            Version = _txtAppVer.Text.Trim(),
            IconPath = _txtAppIcon.Text.Trim(),
            UninstallerPath = _txtAppUninst.Text.Trim(),
            ModifyPath = _txtAppModify.Text.Trim(),
            SourceDir = _txtAppSrc.Text,
            MainExe = exe,
            StartMenuShortcut = _chkStart.Checked,
            DesktopShortcut = _chkDesk.Checked,
        };

        await RunBusy(p => AppInstaller.InstallAsync(_project.MountDir, app, p), onDone: () =>
        {
            _project.Apps.RemoveAll(a => a.Name.Equals(app.Name, StringComparison.OrdinalIgnoreCase));
            _project.Apps.Add(app);
            _project.Save();
            RefreshApps();
        });
    }

    private async void OnBuild(object? sender, EventArgs e)
    {
        if (!_project.Extracted) { Warn("Load an ISO or open a project first."); return; }

        string outIso = _txtOutIso.Text.Trim();
        if (outIso.Length == 0) { Warn("Choose where to save the ISO."); return; }
        outIso = Path.GetFullPath(outIso);

        if (_project.Mounted && !Confirm("The image is mounted. Commit the changes and unmount it before building the ISO?"))
            return;

        await RunBusy(async p =>
        {
            if (_project.Mounted) await WimService.UnmountAsync(_project, commit: true, p);
            await IsoBuilder.BuildAsync(_project.IsoDir, outIso, _project.VolumeLabel, p);
        }, onDone: () => MessageBox.Show(this, "ISO created:\n" + outIso, "WMC", MessageBoxButtons.OK, MessageBoxIcon.Information));
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_busy)
        {
            Warn("An operation is in progress. Wait for it to finish before closing.");
            e.Cancel = true;
            return;
        }

        if (_project.Mounted &&
            !Confirm("An image is still mounted. It will stay mounted if you close now (reopen the project to commit or discard). Close anyway?"))
            e.Cancel = true;
    }

    // ------------------------------------------------------------------ Helpers

    private static async Task<List<WimEdition>> ReadEditionsAsync(WmcProject proj, IProgress<string> log)
    {
        string image = proj.ImageFile ?? throw new InvalidOperationException("install.wim / install.esd not found in the extracted ISO.");
        log.Report("Reading editions...");
        return await WimService.GetEditionsAsync(image, log);
    }

    /// <summary>Runs the work off the UI thread, locks the UI meanwhile and reports errors.</summary>
    private async Task RunBusy(Func<IProgress<string>, Task> work, Action? onDone = null)
    {
        _busy = true;
        _tabs.Enabled = false;
        UseWaitCursor = true;
        try
        {
            await Task.Run(() => work(_progress));
            onDone?.Invoke();
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(this, ex.Message, "WMC - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _busy = false;
            _tabs.Enabled = true;
            UseWaitCursor = false;
            UpdateState();
        }
    }

    /// <summary>The Edition, Apps and Build tabs only exist while an ISO is loaded.</summary>
    private void SyncTabs()
    {
        foreach (var pg in new[] { _pgEdition, _pgApps, _pgBuild })
        {
            bool present = _tabs.TabPages.Contains(pg);
            if (_project.Extracted && !present) _tabs.TabPages.Add(pg);
            else if (!_project.Extracted && present) _tabs.TabPages.Remove(pg);
        }
    }

    private void BindEditions()
    {
        _lvEditions.Items.Clear();
        foreach (var ed in _editions)
            _lvEditions.Items.Add(new ListViewItem(new[] { ed.Index.ToString(), ed.Name }) { Tag = ed.Index });
        if (_lvEditions.Items.Count > 0) _lvEditions.Items[0].Selected = true;
    }

    private void RefreshApps()
    {
        _lvApps.Items.Clear();
        foreach (var a in _project.Apps)
            _lvApps.Items.Add(new ListViewItem(new[]
            {
                a.Name, a.Version, a.Publisher,
                string.IsNullOrWhiteSpace(a.UninstallerPath) ? "No" : "Yes",
                a.InstalledPath,
            }));
    }

    private static bool IsUninstallerName(string fileName) =>
        fileName.StartsWith("unins", StringComparison.OrdinalIgnoreCase);   // unins000.exe, uninst.exe, uninstall.exe

    private void FillExeList(string folder)
    {
        var all = Directory.EnumerateFiles(folder, "*.exe", SearchOption.AllDirectories)
            .Take(5000)
            .Select(f => Path.GetRelativePath(folder, f))
            .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mains = all.Where(f => !IsUninstallerName(Path.GetFileName(f))).Take(300).ToList();

        _cmbExe.Items.Clear();
        _cmbExe.Items.AddRange(mains.Cast<object>().ToArray());

        // Auto-detect an uninstaller shipped with the app (shallowest match wins).
        _txtAppUninst.Text = all.FirstOrDefault(f => IsUninstallerName(Path.GetFileName(f))) ?? "";
        _txtAppIcon.Text = "";
        _txtAppModify.Text = "";

        _txtAppName.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        if (mains.Count > 0) _cmbExe.SelectedIndex = 0;
        else Warn("No .exe files found in that folder.");

        if (_txtAppUninst.Text.Length > 0) Log("Uninstaller detected: " + _txtAppUninst.Text);
    }

    private void AutofillFromExe()
    {
        if (_cmbExe.SelectedItem is not string rel) return;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(Path.Combine(_txtAppSrc.Text, rel));
            if (!string.IsNullOrWhiteSpace(vi.ProductName)) _txtAppName.Text = vi.ProductName.Trim();
            if (!string.IsNullOrWhiteSpace(vi.CompanyName)) _txtAppPub.Text = vi.CompanyName.Trim();
            string? ver = vi.ProductVersion ?? vi.FileVersion;
            if (!string.IsNullOrWhiteSpace(ver)) _txtAppVer.Text = ver.Trim();
        }
        catch { /* no version info: keep what is in the fields */ }
    }

    private void PickFile(TextBox target, string title, string filter)
    {
        using var d = new OpenFileDialog { Title = title, Filter = filter };
        if (Directory.Exists(_txtAppSrc.Text)) d.InitialDirectory = _txtAppSrc.Text;
        if (d.ShowDialog(this) != DialogResult.OK) return;

        // Inside the app folder -> store a relative path; elsewhere -> absolute (copied into the app folder on publish).
        string src = _txtAppSrc.Text.Trim();
        string full = Path.GetFullPath(d.FileName);
        if (src.Length > 0)
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(src)) + "\\";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) full = full[root.Length..];
        }
        target.Text = full;
    }

    private void UpdateState()
    {
        bool extracted = _project.Extracted;
        bool mounted = _project.Mounted;

        _btnMount.Enabled = extracted && !mounted && _lvEditions.SelectedItems.Count > 0;
        _btnUnmountCommit.Enabled = mounted;
        _btnUnmountDiscard.Enabled = mounted;
        _btnPublish.Enabled = mounted;
        _btnBuild.Enabled = extracted;
        _lblAppsHint.Text = mounted ? "" : "Mount an edition in the Edition tab to publish apps.";

        _status.Text = mounted
            ? $" Image mounted (index {_project.ImageIndex}) at {_project.MountDir}"
            : extracted ? " ISO loaded. Image not mounted."
                        : " No project loaded.";
    }

    private void Log(string message) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");

    private void Warn(string message) =>
        MessageBox.Show(this, message, "WMC", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "WMC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
