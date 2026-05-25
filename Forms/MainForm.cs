using SocialMaster.Factories;
using SocialMaster.Helpers;
using SocialMaster.Models;
using SocialMaster.Services;
using SocialMaster.Workers;

namespace SocialMaster.Forms;

public class MainForm : Form
{
    // Services
    private readonly ConfigService _configService = new();
    private SessionManager _sessionManager = null!;
    private WorkerManager _workerManager = null!;

    // UI Controls
    private Panel _filterBar = null!;
    private DataGridView _grid = null!;
    private RichTextBox _log = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private NotifyIcon _trayIcon = null!;
    private System.Windows.Forms.Timer _refreshTimer = null!;
    private Button _btnBatchInterval = null!;

    private string _activeFilter = "全部";
    private readonly Dictionary<string, Button> _filterButtons = new();
    private bool _suppressGridEvents = false;

    public MainForm()
    {
        BuildUi();
        InitializeServices();
        AppLogger.OnLog += OnLogEntry;
        AppLogger.Info("-", "System", $"SocialMaster 已啟動，帳號數: {_sessionManager.Accounts.Count}");
        RefreshGrid();
    }

    // ── UI Construction ──────────────────────────────────────────────────────

    private void BuildUi()
    {
        Text = "SocialMaster";
        var cfg = _configService.Config.Ui;
        Size = new Size(cfg.WindowWidth, cfg.WindowHeight);
        MinimumSize = new Size(960, 560);
        StartPosition = FormStartPosition.CenterScreen;

        // Status bar
        var status = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("就緒");
        status.Items.Add(_statusLabel);

        // ── Filter bar ──────────────────────────────────────────────────────
        _filterBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            BackColor = Color.FromArgb(245, 245, 245),
        };
        var filterFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(8, 5, 8, 5),
        };
        var filterLabel = new Label
        {
            Text = "平台篩選：",
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 0),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(80, 80, 80),
        };
        filterFlow.Controls.Add(filterLabel);

        var platforms = new[] { "全部", "Instagram", "X", "Facebook" };
        foreach (var name in platforms)
        {
            bool isFirst = name == "全部";
            var btn = new Button
            {
                Text = name,
                Width = 90,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = isFirst ? Color.FromArgb(30, 100, 200) : Color.White,
                ForeColor = isFirst ? Color.White : Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 9f, isFirst ? FontStyle.Bold : FontStyle.Regular),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 4, 0),
                Tag = name,
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = isFirst
                ? Color.FromArgb(20, 80, 170)
                : Color.FromArgb(200, 200, 200);
            var captured = name;
            btn.Click += (_, _) => SetFilter(captured);
            filterFlow.Controls.Add(btn);
            _filterButtons[name] = btn;
        }
        _filterBar.Controls.Add(filterFlow);

        // ── Toolbar ─────────────────────────────────────────────────────────
        var toolPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = Color.FromArgb(250, 250, 250),
        };

        // Right-side buttons (added first so docking works correctly)
        var rightFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Padding = new Padding(0, 6, 8, 6),
        };
        rightFlow.Controls.Add(MakeActionBtn("＋  新增帳號", Color.FromArgb(30, 136, 229), (_, _) => AddAccount()));
        rightFlow.Controls.Add(MakeActionBtn("⟳  重新整理", Color.FromArgb(97, 97, 97),   (_, _) => RefreshAll()));

        // Left-side buttons
        var leftFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(8, 6, 0, 6),
        };
        leftFlow.Controls.Add(MakeActionBtn("▶  全部啟動", Color.FromArgb(67, 160, 71),   (_, _) => StartAll()));
        leftFlow.Controls.Add(MakeActionBtn("■  全部停止", Color.FromArgb(229, 57, 53),   (_, _) => StopAll()));
        leftFlow.Controls.Add(new Panel { Width = 10, Height = 1, Margin = new Padding(0) });
        _btnBatchInterval = MakeActionBtn("⏱  更改選取發文間隔", Color.FromArgb(230, 120, 0), (_, _) => BatchUpdateInterval());
        _btnBatchInterval.Enabled = false;
        leftFlow.Controls.Add(_btnBatchInterval);

        toolPanel.Controls.Add(rightFlow);
        toolPanel.Controls.Add(leftFlow);

        // ── Main split container ─────────────────────────────────────────────
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 120,
            Panel2MinSize = 80,
        };
        //Load += (_, _) => split.SplitterDistance = 280;

        // ── Account grid ─────────────────────────────────────────────────────
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            RowHeadersVisible = false,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
            EnableHeadersVisualStyles = false,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 235, 235);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 60, 60);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 235, 235);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(60, 60, 60);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 228, 255);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 20, 20);

        // Select checkbox (for batch operations)
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Select",
            HeaderText = "選取",
            Width = 58,
            ReadOnly = false,
        });
        // Enabled checkbox
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Enabled",
            HeaderText = "啟動",
            Width = 58,
            ReadOnly = false,
        });
        _grid.Columns.AddRange(
            ReadOnlyCol("Id",         "ID",       46),
            ReadOnlyCol("CustomName", "名稱",    110),
            ReadOnlyCol("Status",     "狀態",     130),
            ReadOnlyCol("Platform",   "平台",     86),
            ReadOnlyCol("SourceType", "來源",     70),
            ReadOnlyCol("Interval",   "發文間隔", 110),
            ReadOnlyCol("NextAction", "下次發文", 130),
            ReadOnlyCol("SourceConfig", "來源設定", 0, fill: true)
        );

        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditAccount(e.RowIndex); };
        _grid.MouseUp += Grid_MouseUp;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += OnGridCellValueChanged;

        split.Panel1.Controls.Add(_grid);

        // ── Log panel ────────────────────────────────────────────────────────
        var logHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(240, 240, 240),
        };
        var logHeaderLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
        };
        logHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        logHeaderLayout.Controls.Add(new Label
        {
            Text = "執行日誌",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            Padding = new Padding(8, 0, 0, 0),
        }, 0, 0);
        var btnClear = new Button
        {
            Text = "清除",
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            Margin = new Padding(4, 2, 6, 2),
            Cursor = Cursors.Hand,
        };
        btnClear.FlatAppearance.BorderColor = Color.Silver;
        btnClear.Click += (_, _) => _log.Clear();
        logHeaderLayout.Controls.Add(btnClear, 1, 0);
        logHeader.Controls.Add(logHeaderLayout);

        _log = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(24, 24, 24),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9),
            ScrollBars = RichTextBoxScrollBars.Vertical,

        };
        split.Panel2.Controls.Add(_log);
        split.Panel2.Controls.Add(logHeader);

        // ── System tray ──────────────────────────────────────────────────────
        _trayIcon = new NotifyIcon { Text = "SocialMaster", Icon = SystemIcons.Application, Visible = true };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("顯示", null, (_, _) => BringToFront());
        trayMenu.Items.Add("離開", null, (_, _) => SafeExit());
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => BringToFront();

        // ── Refresh timer ─────────────────────────────────────────────────────
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => RefreshGrid();
        _refreshTimer.Start();

        Controls.Add(split);
        Controls.Add(_filterBar);
        Controls.Add(toolPanel);
        Controls.Add(status);
        FormClosing += MainForm_FormClosing;
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };
    }

    private new void BringToFront()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ── Service Init ─────────────────────────────────────────────────────────

    private void InitializeServices()
    {
        _configService.Load();
        var s = _configService.Config.Settings;
        _sessionManager = new SessionManager(s.AccountsDirectory);
        _sessionManager.Load();
        _workerManager = new WorkerManager(_configService.Config);
        _workerManager.BuildWorkers(_sessionManager.Accounts);
        _workerManager.WorkerStateChanged += (_, e) => SafeInvoke(RefreshGrid);

        Directory.CreateDirectory("logs");
        AppLogger.Initialize(Path.Combine("logs", "social_master_log.txt"));
    }

    // ── Grid ─────────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        _sessionManager.Load();
        _workerManager.BuildWorkers(_sessionManager.Accounts);
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _suppressGridEvents = true;

        var checkedIds = _grid.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Cells["Select"].Value is true && r.Cells["Id"].Value is int)
            .Select(r => (int)r.Cells["Id"].Value!)
            .ToHashSet();

        _grid.Rows.Clear();

        var accounts = _activeFilter == "全部"
            ? _sessionManager.Accounts
            : _sessionManager.Accounts.Where(a => a.Platform == _activeFilter).ToList();

        foreach (var acc in accounts)
        {
            var state = _workerManager.GetState(acc.Id);
            var next = _workerManager.GetNextActionTime(acc.Id);
            var nextStr = state == WorkerState.Waiting && next.HasValue
                ? next.Value.ToString("MM/dd HH:mm")
                : "-";
            var intervalStr = $"{acc.MinIntervalMinutes}~{acc.MaxIntervalMinutes}分";

            var idx = _grid.Rows.Add(
                checkedIds.Contains(acc.Id), // preserve Select state
                acc.IsEnabled,
                acc.Id,
                acc.CustomName,
                StateLabel(state),
                acc.Platform,
                acc.SourceType,
                intervalStr,
                nextStr,
                acc.SourceConfig
            );
            _grid.Rows[idx].DefaultCellStyle.ForeColor = StateColor(state);
        }

        _statusLabel.Text = $"帳號: {accounts.Count()}";
        _btnBatchInterval.Enabled = checkedIds.Count > 0;
        _suppressGridEvents = false;
    }

    private void OnGridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_suppressGridEvents || e.RowIndex < 0) return;
        var colName = _grid.Columns[e.ColumnIndex].Name;

        if (colName == "Select")
        {
            var anySelected = _grid.Rows.Cast<DataGridViewRow>()
                .Any(r => r.Cells["Select"].Value is true);
            _btnBatchInterval.Enabled = anySelected;
            return;
        }

        if (colName == "Enabled")
        {
            if (_grid.Rows[e.RowIndex].Cells["Id"].Value is not int id) return;
            var acc = _sessionManager.Accounts.FirstOrDefault(a => a.Id == id);
            if (acc == null) return;
            acc.IsEnabled = (bool)(_grid.Rows[e.RowIndex].Cells["Enabled"].Value ?? false);
            _sessionManager.UpdateProfile(acc);
            if (acc.IsEnabled)
                _workerManager.BuildWorkers(_sessionManager.Accounts);
            else
                _workerManager.Stop(id);
        }
    }

    private static string StateLabel(WorkerState s) => s switch
    {
        WorkerState.Idle            => "閒置",
        WorkerState.WaitingForLogin => "等待登入",
        WorkerState.Downloading     => "下載中",
        WorkerState.Uploading       => "上傳中",
        WorkerState.Nurturing       => "模擬人類行為中",
        WorkerState.Waiting         => "等待中",
        WorkerState.Error           => "錯誤",
        WorkerState.Stopped         => "停止",
        _ => s.ToString()
    };

    private static Color StateColor(WorkerState s) => s switch
    {
        WorkerState.WaitingForLogin => Color.DeepSkyBlue,
        WorkerState.Uploading       => Color.DodgerBlue,
        WorkerState.Downloading     => Color.MediumSeaGreen,
        WorkerState.Nurturing       => Color.MediumOrchid,
        WorkerState.Waiting         => Color.DarkOrange,
        WorkerState.Error           => Color.Crimson,
        WorkerState.Stopped         => Color.Gray,
        _ => SystemColors.ControlText
    };

    // ── Filter ───────────────────────────────────────────────────────────────

    private void SetFilter(string name)
    {
        _activeFilter = name;
        foreach (var (k, btn) in _filterButtons)
        {
            bool active = k == name;
            btn.BackColor = active ? Color.FromArgb(30, 100, 200) : Color.White;
            btn.ForeColor = active ? Color.White : Color.FromArgb(60, 60, 60);
            btn.Font = new Font("Segoe UI", 9f, active ? FontStyle.Bold : FontStyle.Regular);
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = active
                ? Color.FromArgb(20, 80, 170)
                : Color.FromArgb(200, 200, 200);
        }
        RefreshGrid();
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private void StartAll()
    {
        var filter = _activeFilter == "全部" ? null : _activeFilter;
        foreach (var acc in _sessionManager.Accounts.Where(a => a.IsEnabled))
        {
            if (filter != null && acc.Platform != filter) continue;
            _workerManager.Start(acc.Id);
        }
        RefreshGrid();
    }

    private void StopAll()
    {
        var filter = _activeFilter == "全部" ? null : _activeFilter;
        _workerManager.StopAll(filter);
        RefreshGrid();
    }

    private void AddAccount()
    {
        using var dlg = new AddAccountForm(PlatformFactory.SupportedPlatforms, SourceFactory.SupportedSources);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var acc = _sessionManager.CreateAccount(dlg.SelectedPlatform, dlg.SelectedSource, dlg.SourceConfig);
        acc.CustomName = dlg.CustomName;
        acc.Notes = dlg.Notes;
        acc.FacebookPageUrl = dlg.FacebookPageUrl;
        acc.IsBusinessAccount = dlg.IsBusinessAccount;
        acc.MinIntervalMinutes = dlg.MinIntervalMinutes;
        acc.MaxIntervalMinutes = dlg.MaxIntervalMinutes;
        _sessionManager.UpdateProfile(acc);
        _workerManager.BuildWorkers(_sessionManager.Accounts);
        RefreshGrid();
    }

    private void EditAccount(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Cells["Id"].Value is not int id) return;
        var acc = _sessionManager.Accounts.FirstOrDefault(a => a.Id == id);
        if (acc == null) return;

        using var dlg = new AddAccountForm(PlatformFactory.SupportedPlatforms, SourceFactory.SupportedSources, acc);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        acc.SourceType = dlg.SelectedSource;
        acc.SourceConfig = dlg.SourceConfig;
        acc.CustomName = dlg.CustomName;
        acc.Notes = dlg.Notes;
        acc.FacebookPageUrl = dlg.FacebookPageUrl;
        acc.IsBusinessAccount = dlg.IsBusinessAccount;
        acc.MinIntervalMinutes = dlg.MinIntervalMinutes;
        acc.MaxIntervalMinutes = dlg.MaxIntervalMinutes;
        _sessionManager.UpdateProfile(acc);
        RefreshGrid();
    }

    private void BatchUpdateInterval()
    {
        var selectedIds = _grid.Rows
            .Cast<DataGridViewRow>()
            .Where(r => r.Cells["Select"].Value is true && r.Cells["Id"].Value is int)
            .Select(r => (int)r.Cells["Id"].Value!)
            .ToList();

        if (selectedIds.Count == 0) return;

        // Pre-fill with first selected account's values
        var first = _sessionManager.Accounts.FirstOrDefault(a => a.Id == selectedIds[0]);
        using var dlg = new IntervalForm(first?.MinIntervalMinutes ?? 1150, first?.MaxIntervalMinutes ?? 5250);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var id in selectedIds)
        {
            var acc = _sessionManager.Accounts.FirstOrDefault(a => a.Id == id);
            if (acc == null) continue;
            acc.MinIntervalMinutes = dlg.MinMinutes;
            acc.MaxIntervalMinutes = dlg.MaxMinutes;
            _sessionManager.UpdateProfile(acc);
        }

        // Clear all Select checkboxes
        _suppressGridEvents = true;
        foreach (DataGridViewRow row in _grid.Rows)
            row.Cells["Select"].Value = false;
        _suppressGridEvents = false;
        _btnBatchInterval.Enabled = false;

        RefreshGrid();
        AppLogger.Info("-", "System", $"已更新 {selectedIds.Count} 個帳號的發文間隔");
    }

    private void Grid_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = _grid.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0) return;
        _grid.ClearSelection();
        _grid.Rows[hit.RowIndex].Selected = true;

        if (_grid.Rows[hit.RowIndex].Cells["Id"].Value is not int id) return;
        var menu = new ContextMenuStrip();
        menu.Items.Add("▶ 啟動", null, (_, _) => { _workerManager.Start(id); RefreshGrid(); });
        menu.Items.Add("■ 停止", null, (_, _) => { _workerManager.Stop(id); RefreshGrid(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("✎ 編輯", null, (_, _) => EditAccount(hit.RowIndex));
        menu.Items.Add("📂 開啟帳號資料夾", null, (_, _) =>
        {
            var acc = _sessionManager.Accounts.FirstOrDefault(a => a.Id == id);
            if (acc != null && Directory.Exists(acc.AccountDir))
                System.Diagnostics.Process.Start("explorer.exe", acc.AccountDir);
        });
        menu.Show(_grid, e.Location);
    }

    // ── Log ──────────────────────────────────────────────────────────────────

    private void OnLogEntry(object? sender, LogEntry entry)
    {
        SafeInvoke(() =>
        {
            var color = entry.Level switch
            {
                "ERROR" => Color.Tomato,
                "WARN"  => Color.Gold,
                _       => Color.LightGreen,
            };
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(entry.ToString() + "\n");
            _log.ScrollToCaret();

            if (_log.Lines.Length > 2000)
            {
                _log.Select(0, _log.GetFirstCharIndexFromLine(500));
                _log.SelectedText = "";
            }
        });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SafeInvoke(Action action)
    {
        if (InvokeRequired) Invoke(action);
        else action();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_workerManager.AllWorkers.Any(w => w.IsRunning))
        {
            var r = MessageBox.Show("仍有帳號在執行中，確定要離開嗎？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) { e.Cancel = true; return; }
        }
        SafeExit();
    }

    private void SafeExit()
    {
        _refreshTimer.Stop();
        _workerManager.StopAll();
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private static Button MakeActionBtn(string text, Color back, EventHandler handler)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(110, 32),
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5f),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 8, 0),
            Padding = new Padding(10, 0, 10, 0),
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += handler;
        return btn;
    }

    private static DataGridViewTextBoxColumn ReadOnlyCol(
        string name, string header, int width, bool fill = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            DataPropertyName = name,
            ReadOnly = true,
        };
        if (fill) col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        else col.Width = width;
        return col;
    }
}
