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
    private readonly TabPage _pgFiles = new("Files");
    private readonly TabPage _pgDefaults = new("Defaults");
    private readonly TabPage _pgBuild = new("Build");

    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Bottom,
        Height = 110,
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
    private readonly ComboBox _cmbWim = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _btnWimRescan = Btn("Rescan");
    private bool _loadingWimList;

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

    // Metadata tab
    private readonly TabPage _pgMeta = new("Metadata");
    private readonly ListView _lvMeta = NewListView(("#", 40), ("Original", 60), ("Name", 230), ("Description", 320));
    private readonly TextBox _txtMetaName = new(), _txtMetaDesc = new(), _txtMetaDispName = new(),
                             _txtMetaDispDesc = new(), _txtMetaFlags = new();
    private readonly TextBox _txtMetaExtra = new() { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Height = 64 };
    private readonly Button _btnMetaUp = Btn("Move up"), _btnMetaDown = Btn("Move down"),
                            _btnMetaRemove = Btn("Remove"), _btnMetaReload = Btn("Reload");
    private readonly Button _btnMetaApply = Btn("Apply changes", 110);
    private readonly Label _lblMetaHint = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private List<ImageEdit> _edits = new();
    private List<ImageMeta>? _pendingMetas;
    private int _origCount;
    private bool _loadingEditor;

    // Files tab
    private readonly TextBox _txtFileSrc = new() { ReadOnly = true };
    private readonly List<string> _fileSources = new();
    private readonly ComboBox _cmbFileDest = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly CheckBox _chkOverwrite = new() { Text = "Overwrite existing files", Checked = true, AutoSize = true };
    private readonly Button _btnAddFiles = Btn("Add to image", 110);
    private readonly Label _lblFilesHint = new() { AutoSize = true, Margin = new Padding(8, 6, 0, 0) };
    private readonly ListView _lvFiles = NewListView(("Source", 340), ("Destination in image", 420));

    // Defaults tab
    private readonly Label _lblDefaultsHint = new() { Dock = DockStyle.Top, Height = 22, Padding = new Padding(8, 6, 0, 0) };
    private readonly Label _lblShellCurrent = new() { AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
    private readonly ComboBox _cmbShell = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Button _btnShellApply = Btn("Apply shell", 100), _btnShellReset = Btn("Reset to explorer", 120),
                            _btnShellRead = Btn("Read current", 100);
    private readonly TextBox _txtWall = new() { ReadOnly = true };
    private readonly CheckBox _chkWall4k = new()
    {
        Text = "Also replace the 4K variants (Windows\\Web\\4K\\Wallpaper\\Windows\\img0_*.jpg)",
        Checked = true,
        AutoSize = true,
    };
    private readonly Button _btnWallApply = Btn("Apply wallpaper", 120);

    // Build tab
    private readonly TextBox _txtOutIso = new();
    private readonly Button _btnBuild = Btn("Build ISO", 100);

    public MainForm()
    {
        Text = "WMC - Windows Mod Creator";
        Font = new Font("Tahoma", 8.25f);
        BackColor = SystemColors.Control;
        ClientSize = new Size(856, 520);       // default size (about 860 x 560 with the window frame)
        MinimumSize = new Size(720, 520);
        StartPosition = FormStartPosition.CenterScreen;

        _progress = new Progress<string>(Log);

        BuildProjectTab();
        BuildEditionTab();
        BuildMetadataTab();
        BuildAppsTab();
        BuildFilesTab();
        BuildDefaultsTab();
        BuildBuildTab();

        _tabs.TabPages.Add(_pgProject);
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab is { } pg) FitTabScroll(pg); };

        // Fill control first, then the Bottom ones (WinForms docking order).
        Controls.Add(_tabs);
        Controls.Add(_log);
        Controls.Add(_status);

        FormClosing += OnFormClosing;
        UpdateState();
        Log("Ready. Choose a Windows ISO in the Project tab and click Load ISO.");
        Log("Log file: " + SessionLog.FilePath);
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

    private static void AddRow(TableLayoutPanel t, string label, Control ctl, Control? btn = null)
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

        // Which WIM/ESD of the ISO to modify
        var top = NewTable();
        AddRow(top, "WIM file:", _cmbWim, _btnWimRescan);
        AddWide(top, new Label
        {
            Text = "The image to modify. install.wim / install.esd hold the Windows editions; boot.wim is the Windows PE / Setup environment.",
            AutoSize = true,
        });
        _cmbWim.SelectedIndexChanged += OnWimChanged;
        _btnWimRescan.Click += (_, _) => { BindWimList(); Log($"Found {_cmbWim.Items.Count} WIM/ESD file(s) in the ISO."); };

        _pgEdition.Controls.Add(_lvEditions);   // Fill first
        _pgEdition.Controls.Add(buttons);
        _pgEdition.Controls.Add(top);           // last added = top-most
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

    private void BuildFilesTab()
    {
        var t = NewTable();

        var btnFiles = Btn("Files...", 80);
        btnFiles.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Select files to add to the image", Multiselect = true };
            if (d.ShowDialog(this) == DialogResult.OK) SetFileSources(d.FileNames);
        };
        var btnFolder = Btn("Folder...", 80);
        btnFolder.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Folder to add to the image (copied as a subfolder with the same name)" };
            if (d.ShowDialog(this) == DialogResult.OK) SetFileSources(new[] { d.SelectedPath });
        };
        var pick = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        pick.Controls.Add(btnFiles);
        pick.Controls.Add(btnFolder);

        _cmbFileDest.Items.AddRange(FileInjector.CommonDestinations.Cast<object>().ToArray());
        _cmbFileDest.Text = FileInjector.CommonDestinations[0];

        AddRow(t, "Files / folder:", _txtFileSrc, pick);
        AddRow(t, "Destination folder:", _cmbFileDest);
        AddWide(t, new Label
        {
            Text = "Destination is a folder inside the image; type any path, e.g. C:\\Users\\Public\\Desktop.\r\n" +
                   "Public\\Desktop shows the files to every user; Default\\Desktop only to users created after the install.\r\n" +
                   "A folder is copied as a subfolder with its own name.",
            AutoSize = true,
        });
        AddWide(t, _chkOverwrite);

        var addRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        addRow.Controls.Add(_btnAddFiles);
        addRow.Controls.Add(_lblFilesHint);
        AddWide(t, addRow);
        _btnAddFiles.Click += OnAddFiles;

        var listGroup = new GroupBox { Text = "Added files", Dock = DockStyle.Fill, Padding = new Padding(6, 2, 6, 6) };
        listGroup.Controls.Add(_lvFiles);

        _pgFiles.Padding = new Padding(6);
        _pgFiles.Controls.Add(listGroup);                                  // Fill first
        _pgFiles.Controls.Add(NewGroup("Add to image", t));                // last added = top-most
    }

    private void BuildDefaultsTab()
    {
        // Default shell
        var shell = NewTable();
        var btnBrowse = Btn("Browse image...", 110);
        btnBrowse.Click += OnShellBrowse;
        AddRow(shell, "Current shell:", _lblShellCurrent);
        AddRow(shell, "New shell:", _cmbShell, btnBrowse);
        AddWide(shell, new Label
        {
            Text = "Path as seen inside Windows, e.g. C:\\Program Files\\MyShell\\shell.exe (arguments allowed). The program must already exist in the\r\n" +
                   "image: publish it in Apps or add it in Files first, or use Browse image. explorer.exe is the Windows default.",
            AutoSize = true,
        });
        var shellButtons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        shellButtons.Controls.AddRange(new Control[] { _btnShellApply, _btnShellReset, _btnShellRead });
        AddWide(shell, shellButtons);

        _cmbShell.DropDown += (_, _) => FillShellChoices();
        _btnShellApply.Click += OnShellApply;
        _btnShellReset.Click += OnShellReset;
        _btnShellRead.Click += OnShellRead;

        // Default wallpaper
        var wall = NewTable();
        var btnWall = Btn("Browse...");
        btnWall.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Select the new default wallpaper", Filter = "JPEG image (*.jpg)|*.jpg" };
            if (d.ShowDialog(this) == DialogResult.OK) _txtWall.Text = d.FileName;
        };
        AddRow(wall, "JPG image:", _txtWall, btnWall);
        AddWide(wall, new Label
        {
            Text = "Only .jpg. The file is renamed to img0.jpg and replaces C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg.",
            AutoSize = true,
        });
        AddWide(wall, _chkWall4k);
        _btnWallApply.Margin = new Padding(0, 6, 0, 0);
        AddWide(wall, _btnWallApply);
        _btnWallApply.Click += OnWallApply;

        _pgDefaults.Padding = new Padding(6);
        _pgDefaults.Controls.Add(NewGroup("Default wallpaper", wall));
        _pgDefaults.Controls.Add(NewGroup("Default shell (Winlogon\\Shell)", shell));
        _pgDefaults.Controls.Add(_lblDefaultsHint);   // last added = top-most
    }

    private void BuildMetadataTab()
    {
        _lvMeta.Dock = DockStyle.Top;
        _lvMeta.Height = 130;

        var listButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 4, 0, 4) };
        listButtons.Controls.AddRange(new Control[] { _btnMetaUp, _btnMetaDown, _btnMetaRemove, _btnMetaReload });

        var t = NewTable();
        AddRow(t, "Name:", _txtMetaName);
        AddRow(t, "Description:", _txtMetaDesc);
        AddRow(t, "Display name:", _txtMetaDispName);
        AddRow(t, "Display description:", _txtMetaDispDesc);
        AddRow(t, "Flags:", _txtMetaFlags);
        AddRow(t, "Extra properties:", _txtMetaExtra);
        AddWide(t, new Label
        {
            Text = "Extra properties: one KEY=VALUE per line, e.g. WINDOWS/PRODUCTNAME=My Windows.\r\n" +
                   "Empty fields are left unchanged. The order of the list above is the new index order.",
            AutoSize = true,
        });

        var applyRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        applyRow.Controls.Add(_btnMetaApply);
        applyRow.Controls.Add(_lblMetaHint);

        foreach (var tb in new[] { _txtMetaName, _txtMetaDesc, _txtMetaDispName, _txtMetaDispDesc, _txtMetaFlags, _txtMetaExtra })
            tb.TextChanged += (_, _) => OnEditorChanged();

        _lvMeta.SelectedIndexChanged += (_, _) => { LoadEditor(); UpdateState(); };
        _btnMetaUp.Click += (_, _) => MoveEdit(-1);
        _btnMetaDown.Click += (_, _) => MoveEdit(+1);
        _btnMetaRemove.Click += OnMetaRemove;
        _btnMetaReload.Click += OnMetaReload;
        _btnMetaApply.Click += OnMetaApply;

        _pgMeta.Padding = new Padding(6);
        _pgMeta.Controls.Add(applyRow);                                          // added in reverse: last = top-most
        _pgMeta.Controls.Add(NewGroup("Selected edition", t));
        _pgMeta.Controls.Add(listButtons);
        _pgMeta.Controls.Add(_lvMeta);
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
            proj.SelectedImage = proj.FindImages().FirstOrDefault() ?? "";
            proj.Save();
            editions = await ReadEditionsAsync(proj, p);
            _pendingMetas = await WimMetadata.ReadAsync(proj.ImageFile!);
        }, onDone: () =>
        {
            _project = proj;
            _editions = editions ?? new();
            BindEditions();
            _txtOutIso.Text = Path.Combine(proj.WorkDir, "WMC.iso");
            RefreshApps();
            RefreshFiles();
            BindWimList();
            BindMeta(_pendingMetas);
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
            _pendingMetas = null;
            if (proj.Extracted && !proj.Mounted)
            {
                editions = await ReadEditionsAsync(proj, p);
                _pendingMetas = await WimMetadata.ReadAsync(proj.ImageFile!);
            }
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
            RefreshFiles();
            BindWimList();
            BindMeta(_pendingMetas);
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
            BindWimList();   // an ESD gets converted to a WIM when mounted
            _tabs.SelectedTab = _pgApps;
        });
    }

    private async void OnUnmount(bool commit)
    {
        if (!_project.Mounted) return;
        if (!commit && !Confirm("Discard all changes made to the image?")) return;
        await RunBusy(p => WimService.UnmountAsync(_project, commit, p));
        if (!_project.Mounted) await ReloadImageDataAsync();   // the image content (and edition list) may have changed
    }

    // ---- WIM file selection

    private async void OnWimChanged(object? sender, EventArgs e)
    {
        if (_loadingWimList || _busy || _project.Mounted) return;
        if (_cmbWim.SelectedItem is not string rel) return;
        if (rel.Equals(_project.SelectedImage, StringComparison.OrdinalIgnoreCase)) return;

        _project.SelectedImage = rel;
        _project.Save();
        Log("Selected image: " + rel);
        await ReloadImageDataAsync();
    }

    /// <summary>Re-reads the editions and metadata of the selected image. Stale lists are cleared first.</summary>
    private async Task ReloadImageDataAsync()
    {
        var proj = _project;
        if (!proj.Extracted) return;

        _editions = new();
        BindEditions();
        BindMeta(null);

        List<WimEdition>? editions = null;
        await RunBusy(async p =>
        {
            editions = await ReadEditionsAsync(proj, p);
            _pendingMetas = await WimMetadata.ReadAsync(proj.ImageFile!);
        }, onDone: () =>
        {
            _editions = editions ?? new();
            BindEditions();
            BindMeta(_pendingMetas);
        });
    }

    // ---- Files tab logic

    private void SetFileSources(IEnumerable<string> paths)
    {
        _fileSources.Clear();
        _fileSources.AddRange(paths);
        _txtFileSrc.Text = string.Join("; ", _fileSources);
    }

    private async void OnAddFiles(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }
        if (_fileSources.Count == 0) { Warn("Choose the files or the folder to add."); return; }

        string dest = _cmbFileDest.Text.Trim();
        if (dest.Length == 0) { Warn("Enter the destination folder inside the image."); return; }

        var sources = _fileSources.ToList();
        bool overwrite = _chkOverwrite.Checked;
        List<AddedFile>? added = null;

        await RunBusy(async p => added = await FileInjector.AddAsync(_project.MountDir, sources, dest, overwrite, p), onDone: () =>
        {
            if (added is null) return;
            foreach (var a in added)
            {
                _project.Files.RemoveAll(f => f.Destination.Equals(a.Destination, StringComparison.OrdinalIgnoreCase));
                _project.Files.Add(a);
            }
            _project.Save();
            RefreshFiles();
        });
    }

    // ---- Defaults tab logic

    private void FillShellChoices()
    {
        string text = _cmbShell.Text;
        _cmbShell.Items.Clear();
        _cmbShell.Items.Add(ShellService.DefaultShell);
        foreach (var a in _project.Apps.Where(a => a.InstalledPath.Length > 0 && a.MainExe.Length > 0))
            _cmbShell.Items.Add(Path.Combine(a.InstalledPath, a.MainExe));
        _cmbShell.Text = text;
    }

    private void OnShellBrowse(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }

        using var d = new OpenFileDialog
        {
            Title = "Select the shell program inside the mounted image",
            InitialDirectory = _project.MountDir,
            Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*",
            DereferenceLinks = false,
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        string mount = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_project.MountDir)) + "\\";
        string full = Path.GetFullPath(d.FileName);
        if (!full.StartsWith(mount, StringComparison.OrdinalIgnoreCase))
        {
            Warn("Pick a file inside the mounted image:\n" + _project.MountDir);
            return;
        }

        _cmbShell.Text = @"C:\" + full[mount.Length..];
    }

    private void ShowShell(string? value) => _lblShellCurrent.Text = value ?? "(not set)";

    private async void OnShellRead(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }

        string? current = null;
        await RunBusy(async p => current = await ShellService.GetAsync(_project.MountDir, p),
            onDone: () => ShowShell(current));
    }

    private async void OnShellApply(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }

        string command = _cmbShell.Text.Trim();
        if (command.Length == 0) { Warn("Enter the shell to use."); return; }

        string? result = null;
        await RunBusy(async p => result = await ShellService.SetAsync(_project.MountDir, command, p),
            onDone: () => ShowShell(result));
    }

    private void OnShellReset(object? sender, EventArgs e)
    {
        _cmbShell.Text = ShellService.DefaultShell;
        OnShellApply(sender, e);
    }

    private async void OnWallApply(object? sender, EventArgs e)
    {
        if (!_project.Mounted) { Warn("Mount an edition in the Edition tab first."); return; }

        string jpg = _txtWall.Text.Trim();
        if (jpg.Length == 0) { Warn("Choose a .jpg image."); return; }

        bool all = _chkWall4k.Checked;
        await RunBusy(p => WallpaperService.SetAsync(_project.MountDir, jpg, all, p));
    }

    // ---- Metadata tab logic

    private void BindMeta(IEnumerable<ImageMeta>? metas)
    {
        _edits = (metas ?? Enumerable.Empty<ImageMeta>()).Select(m => new ImageEdit(m)).ToList();
        _origCount = _edits.Count;
        RefreshMetaList(0);
    }

    private void RefreshMetaList(int select)
    {
        _lvMeta.BeginUpdate();
        _lvMeta.Items.Clear();
        for (int i = 0; i < _edits.Count; i++)
        {
            var ed = _edits[i];
            _lvMeta.Items.Add(new ListViewItem(new[] { (i + 1).ToString(), ed.OriginalIndex.ToString(), ed.Name, ed.Description }));
        }
        _lvMeta.EndUpdate();

        if (_edits.Count > 0) _lvMeta.Items[Math.Clamp(select, 0, _edits.Count - 1)].Selected = true;
        else LoadEditor();
        UpdateState();
    }

    private int SelectedEditIndex => _lvMeta.SelectedIndices.Count > 0 ? _lvMeta.SelectedIndices[0] : -1;

    private void LoadEditor()
    {
        _loadingEditor = true;
        try
        {
            int i = SelectedEditIndex;
            var ed = i >= 0 ? _edits[i] : null;
            _txtMetaName.Text = ed?.Name ?? "";
            _txtMetaDesc.Text = ed?.Description ?? "";
            _txtMetaDispName.Text = ed?.DisplayName ?? "";
            _txtMetaDispDesc.Text = ed?.DisplayDescription ?? "";
            _txtMetaFlags.Text = ed?.Flags ?? "";
            _txtMetaExtra.Text = ed?.ExtraProperties ?? "";
        }
        finally { _loadingEditor = false; }
    }

    private void OnEditorChanged()
    {
        if (_loadingEditor) return;
        int i = SelectedEditIndex;
        if (i < 0) return;

        var ed = _edits[i];
        ed.Name = _txtMetaName.Text;
        ed.Description = _txtMetaDesc.Text;
        ed.DisplayName = _txtMetaDispName.Text;
        ed.DisplayDescription = _txtMetaDispDesc.Text;
        ed.Flags = _txtMetaFlags.Text;
        ed.ExtraProperties = _txtMetaExtra.Text;

        _lvMeta.Items[i].SubItems[2].Text = ed.Name;
        _lvMeta.Items[i].SubItems[3].Text = ed.Description;
    }

    private void MoveEdit(int delta)
    {
        int i = SelectedEditIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _edits.Count) return;

        (_edits[i], _edits[j]) = (_edits[j], _edits[i]);
        RefreshMetaList(j);
    }

    private void OnMetaRemove(object? sender, EventArgs e)
    {
        int i = SelectedEditIndex;
        if (i < 0) return;
        if (_edits.Count <= 1) { Warn("The image must keep at least one edition."); return; }
        if (!Confirm($"Remove '{_edits[i].Name}' from the image? (applied when you click Apply changes)")) return;

        _edits.RemoveAt(i);
        RefreshMetaList(i);
    }

    private async void OnMetaReload(object? sender, EventArgs e)
    {
        string? image = _project.ImageFile;
        if (image is null) { Warn("No image found."); return; }

        await RunBusy(async _ => _pendingMetas = await WimMetadata.ReadAsync(image),
            onDone: () => BindMeta(_pendingMetas));
    }

    private async void OnMetaApply(object? sender, EventArgs e)
    {
        if (_project.Mounted) { Warn("Unmount the image first (Edition tab)."); return; }
        string? image = _project.ImageFile;
        if (image is null) { Warn("No image found."); return; }
        if (_edits.Count == 0) { Warn("Nothing to apply."); return; }

        try { _ = _edits.Any(x => x.IsDirty); }   // validates the KEY=VALUE lines
        catch (FormatException ex) { Warn(ex.Message); return; }

        bool rebuild = WimMetadata.NeedsRebuild(image, _edits, _origCount);
        string question = rebuild
            ? "The editions were reordered/removed (or the image is an ESD), so the image will be re-exported with DISM. This can take a long time. Continue?"
            : "Apply the metadata changes to the image?";
        if (!Confirm(question)) return;

        var edits = _edits.ToList();
        int origCount = _origCount;
        List<WimEdition>? editions = null;

        await RunBusy(async p =>
        {
            await WimMetadata.ApplyAsync(_project, edits, origCount, p);
            string img = _project.ImageFile ?? throw new InvalidOperationException("install.wim not found after applying.");
            _pendingMetas = await WimMetadata.ReadAsync(img);
            editions = await WimService.GetEditionsAsync(img, null);
        }, onDone: () =>
        {
            _editions = editions ?? new();
            BindEditions();
            BindWimList();   // a rebuilt ESD becomes a WIM
            BindMeta(_pendingMetas);
        });
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
        string image = proj.ImageFile ?? throw new InvalidOperationException("No .wim / .esd image found in the extracted ISO.");
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
            SessionLog.Write(ex.ToString());   // full stack trace, file only
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

    /// <summary>
    /// Lets a tab scroll instead of clipping its content when the window is too small for it.
    /// Docked content keeps filling the tab when there is more room than needed.
    /// </summary>
    private void FitTabScroll(TabPage page)
    {
        int width = Math.Max(300, _tabs.DisplayRectangle.Width - page.Padding.Horizontal);
        int need = page.Padding.Vertical;

        foreach (Control c in page.Controls)
        {
            if (c.Dock == DockStyle.Fill) need += 90;   // lists keep a usable minimum height
            else need += c.AutoSize ? c.GetPreferredSize(new Size(width, 0)).Height : c.Height;
        }

        page.AutoScroll = true;
        page.AutoScrollMinSize = new Size(0, need);
    }

    /// <summary>Every tab except Project only exists while an ISO is loaded.</summary>
    private void SyncTabs()
    {
        foreach (var pg in new[] { _pgEdition, _pgMeta, _pgApps, _pgFiles, _pgDefaults, _pgBuild })
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

    /// <summary>Fills the WIM combo with every .wim/.esd found in the ISO and selects the current image.</summary>
    private void BindWimList()
    {
        _loadingWimList = true;
        try
        {
            _cmbWim.Items.Clear();
            if (!_project.Extracted) return;

            foreach (var rel in _project.FindImages()) _cmbWim.Items.Add(rel);

            string? current = _project.ImageFile is { } f ? Path.GetRelativePath(_project.IsoDir, f) : null;
            int sel = -1;
            for (int i = 0; i < _cmbWim.Items.Count; i++)
                if (current is not null && string.Equals((string)_cmbWim.Items[i]!, current, StringComparison.OrdinalIgnoreCase))
                    sel = i;

            _cmbWim.SelectedIndex = sel;
            if (sel >= 0) _project.SelectedImage = (string)_cmbWim.Items[sel]!;
        }
        finally { _loadingWimList = false; }
    }

    private void RefreshFiles()
    {
        _lvFiles.Items.Clear();
        foreach (var f in _project.Files)
            _lvFiles.Items.Add(new ListViewItem(new[] { f.Source, f.Destination }));
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

        _btnAddFiles.Enabled = mounted;
        _lblFilesHint.Text = mounted ? "" : "Mount an edition in the Edition tab to add files.";
        _cmbWim.Enabled = extracted && !mounted;
        _btnWimRescan.Enabled = extracted && !mounted;

        _btnShellApply.Enabled = mounted;
        _btnShellReset.Enabled = mounted;
        _btnShellRead.Enabled = mounted;
        _btnWallApply.Enabled = mounted;
        _lblDefaultsHint.Text = mounted ? "" : "Mount an edition in the Edition tab to change these defaults.";
        if (!mounted) _lblShellCurrent.Text = "(mount an edition to read it)";

        int sel = SelectedEditIndex;
        _btnMetaApply.Enabled = extracted && !mounted && _edits.Count > 0;
        _btnMetaReload.Enabled = extracted && !mounted;
        _btnMetaUp.Enabled = !mounted && sel > 0;
        _btnMetaDown.Enabled = !mounted && sel >= 0 && sel < _edits.Count - 1;
        _btnMetaRemove.Enabled = !mounted && sel >= 0 && _edits.Count > 1;
        _lblMetaHint.Text = mounted ? "Unmount the image to edit its metadata."
            : WimMetadata.FindWimlib() is null ? "wimlib-imagex.exe not found (needed to change properties) - see tools\\wimlib\\README.txt."
            : "";

        string image = _project.ImageFile is { } img ? Path.GetRelativePath(_project.IsoDir, img) : "(none)";
        _status.Text = mounted
            ? $" Mounted: {image}, index {_project.ImageIndex}, at {_project.MountDir}"
            : extracted ? $" ISO loaded. Image: {image}. Not mounted."
                        : " No project loaded.";
    }

    private void Log(string message)
    {
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
        SessionLog.Write(message);
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "WMC", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private bool Confirm(string message) =>
        MessageBox.Show(this, message, "WMC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
}
