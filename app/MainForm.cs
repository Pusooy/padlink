using System.Diagnostics;

namespace PadLink;

/// <summary>主设置窗口：现代化深色 UI，连接 / 应用 DIY / 常规 三页。</summary>
internal sealed class MainForm : Form
{
    private readonly Button _navConn, _navApps, _navGeneral;
    private readonly Control _pageConn, _pageApps, _pageGeneral;
    private readonly Label _urlLabel, _ipLabel, _connCountLabel;
    private readonly DataGridView _grid;
    private readonly CheckBox _autostartBox, _autoPageBox;
    private System.Windows.Forms.Timer? _refreshTimer;

    private static readonly Color Accent = Color.FromArgb(92, 141, 255);
    private static readonly Color Bg = Color.FromArgb(22, 23, 29);
    private static readonly Color Card = Color.FromArgb(28, 30, 38);
    private static readonly Color Line = Color.FromArgb(48, 51, 63);
    private static readonly Color TextDim = Color.FromArgb(139, 143, 154);

    public MainForm()
    {
        Text = "PadLink 设置";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 520);
        BackColor = Bg;
        Font = new Font("Segoe UI", 10f);
        MinimumSize = new Size(680, 460);

        // 侧边导航
        var nav = new Panel { Dock = DockStyle.Left, Width = 186, BackColor = Color.FromArgb(18, 19, 25) };
        var brand = new Label
        {
            Text = "PadLink",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Accent,
            Location = new Point(20, 26),
            AutoSize = true,
        };
        _navConn = NavButton("连接", true);
        _navApps = NavButton("应用管理", false);
        _navGeneral = NavButton("常规", false);
        _navConn.Location = new Point(12, 80);
        _navApps.Location = new Point(12, 132);
        _navGeneral.Location = new Point(12, 184);
        nav.Controls.AddRange([brand, _navConn, _navApps, _navGeneral]);

        // ---- 连接页 ----
        _pageConn = FlowPage();
        _urlLabel = BigLabel();
        _urlLabel.Text = TrayContext.BaseUrl;
        _ipLabel = DimLabel();
        _connCountLabel = DimLabel();
        var btnOpen = FlatButton("打开控制页", Accent, Color.White);
        btnOpen.Click += (s, e) =>
        {
            try { Process.Start(new ProcessStartInfo(TrayContext.BaseUrl) { UseShellExecute = true }); }
            catch { MessageBox.Show("打开失败，请手动在浏览器访问 " + TrayContext.BaseUrl); }
        };
        var btnCopy = FlatButton("复制地址", Card, Color.White);
        btnCopy.Click += (s, e) => { Clipboard.SetText(TrayContext.BaseUrl); _connCountLabel.Text = "已复制 ✓"; };
        LayoutColumn(_pageConn, [
            Section("手机连接"),
            _urlLabel, _ipLabel, _connCountLabel,
            Row(btnOpen, btnCopy),
            Section("使用步骤"),
            DimLabel("1. 手机与电脑连同一 WiFi\n2. 手机浏览器访问上方地址\n3. iPhone：分享 → 添加到主屏幕（全屏 App 体验）\n4. 连接一次后自动记住并重连"),
        ]);

        // ---- 应用管理页 ----
        _pageApps = NewPage();
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Card,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            RowTemplate = { Height = 34 },
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            GridColor = Line,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(36, 39, 50);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(36, 39, 50);
        _grid.DefaultCellStyle.BackColor = Card;
        _grid.DefaultCellStyle.ForeColor = Color.White;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(50, 56, 74);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _grid.Columns.Add("name", "应用名称（手机上显示）");
        _grid.Columns.Add("cmd", "启动命令（exe 路径 / PATH 程序名 / URI）");
        LoadApps();
        var btnSave = FlatButton("保存", Accent, Color.White);
        btnSave.Click += (s, e) => SaveApps();
        var btnAdd = FlatButton("添加一行", Card, Color.White);
        btnAdd.Click += (s, e) => _grid.Rows.Add("", "");
        var btnDel = FlatButton("删除选中行", Card, Color.White);
        btnDel.Click += (s, e) =>
        {
            foreach (DataGridViewRow r in _grid.SelectedRows)
                if (!r.IsNewRow) _grid.Rows.Remove(r);
        };
        var tip = DimLabel("提示：cmd 可以是完整路径、PATH 里的程序名（如 notepad.exe）或 URI（如 ms-screenclip:）。保存后手机端刷新 Dock 即可看到。");
        var gridWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 8) };
        gridWrap.Controls.Add(_grid);
        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(0, 4, 0, 0), BackColor = Color.Transparent };
        foreach (var b in new[] { btnSave, btnAdd, btnDel }) { b.AutoSize = false; b.Width = 130; b.Height = 38; btnRow.Controls.Add(b); }
        _pageApps.Controls.Add(gridWrap);
        _pageApps.Controls.Add(btnRow);
        _pageApps.Controls.Add(tip);
        tip.Dock = DockStyle.Bottom;
        btnRow.Dock = DockStyle.Bottom;
        tip.Height = 60;

        // ---- 常规页 ----
        _pageGeneral = FlowPage();
        _autostartBox = new CheckBox
        {
            Text = "开机自动启动 PadLink",
            ForeColor = Color.White,
            AutoSize = true,
            Checked = Autostart.IsEnabled(),
        };
        _autostartBox.CheckedChanged += (s, e) =>
        {
            if ((Autostart.IsEnabled()) != _autostartBox.Checked) Autostart.Toggle();
        };
        var btnLog = FlatButton("打开日志文件夹", Card, Color.White);
        btnLog.Click += (s, e) => Process.Start("explorer.exe", AppContext.BaseDirectory);
        _autoPageBox = new CheckBox
        {
            Text = "启动时自动打开控制页（默认关）",
            ForeColor = Color.White,
            AutoSize = true,
            Checked = AppSettings.AutoOpenPage,
        };
        _autoPageBox.CheckedChanged += (s, e) => AppSettings.AutoOpenPage = _autoPageBox.Checked;
        LayoutColumn(_pageGeneral, [
            Section("常规"),
            _autostartBox,
            _autoPageBox,
            Row(btnLog),
            Section("关于"),
            DimLabel($"PadLink v1.1 · .NET 8 + WinForms\n端口 {TrayContext.Port} · WebSocket 明文协议，仅限可信局域网使用"),
        ]);

        foreach (var p in new[] { _pageConn, _pageApps, _pageGeneral }) Controls.Add(p);
        Controls.Add(nav);
        ShowPage(_pageConn, _navConn);

        _navConn.Click += (s, e) => ShowPage(_pageConn, _navConn);
        _navApps.Click += (s, e) => ShowPage(_pageApps, _navApps);
        _navGeneral.Click += (s, e) => ShowPage(_pageGeneral, _navGeneral);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _refreshTimer.Tick += (s, e) => UpdateConn();
        _refreshTimer.Start();
        UpdateConn();
    }

    private void UpdateConn()
    {
        _connCountLabel.Text = $"当前连接设备：{WebSocketHandler.ConnectedCount} 台";
        _ipLabel.Text = $"局域网 IP：{TrayContext.LocalIP}（IP 变了请重新连接）";
    }

    // ---- UI 辅助 ----
    private static Button NavButton(string text, bool active) => new()
    {
        Text = "  " + text,
        TextAlign = ContentAlignment.MiddleLeft,
        Size = new Size(166, 40),
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        BackColor = active ? Color.FromArgb(40, 46, 62) : Color.Transparent,
        ForeColor = active ? Accent : TextDim,
        Font = new Font("Segoe UI", 10.5f),
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
    };
    private void ShowPage(Control page, Button active)
    {
        _pageConn.Visible = page == _pageConn;
        _pageApps.Visible = page == _pageApps;
        _pageGeneral.Visible = page == _pageGeneral;
        foreach (var (b, on) in new[] { (_navConn, page == _pageConn), (_navApps, page == _pageApps), (_navGeneral, page == _pageGeneral) })
        {
            b.BackColor = on ? Color.FromArgb(40, 46, 62) : Color.Transparent;
            b.ForeColor = on ? Accent : TextDim;
            b.FlatAppearance.BorderSize = 0;
        }
    }
    private static Panel NewPage() => new() { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(24, 18, 24, 12), Visible = false };
    private static FlowLayoutPanel FlowPage() => new()
    {
        Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(24, 18, 24, 12), Visible = false,
        FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
    };
    private static Label Section(string t) => new() { Text = t, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.White, AutoSize = true, Margin = new Padding(0, 6, 0, 10) };
    private static Label DimLabel(string? text = null) => new() { Text = text ?? "", ForeColor = TextDim, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
    private static Label BigLabel() => new() { Font = new Font("Segoe UI", 16f, FontStyle.Bold), ForeColor = Accent, AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
    private static Button FlatButton(string text, Color back, Color fore) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, BackColor = back, ForeColor = fore,
        Font = new Font("Segoe UI", 10f), Cursor = Cursors.Hand, AutoSize = true, Margin = new Padding(0, 8, 10, 0),
    };
    private static FlowLayoutPanel Row(params Button[] buttons)
    {
        var f = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, BackColor = Color.Transparent, WrapContents = false };
        foreach (var b in buttons) f.Controls.Add(b);
        return f;
    }
    private static void LayoutColumn(Control page, Control[] items)
    {
        foreach (var c in items) { c.Dock = c is Label l && !l.AutoSize ? DockStyle.Top : DockStyle.Top; page.Controls.Add(c); }
    }

    // ---- apps.json ----
    private void LoadApps()
    {
        _grid.Rows.Clear();
        foreach (dynamic a in AppLauncher.ListAppsRaw())
            _grid.Rows.Add(a.name, a.cmd);
        if (_grid.Rows.Count == 0) _grid.Rows.Add("", "");
    }
    private void SaveApps()
    {
        try
        {
            var list = new List<(string Name, string Cmd)>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.IsNewRow) continue;
                var name = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "";
                var cmd = Convert.ToString(r.Cells[1].Value)?.Trim() ?? "";
                if (name.Length > 0 && cmd.Length > 0) list.Add((name, cmd));
            }
            AppLauncher.SaveApps(list);
            MessageBox.Show("已保存，手机端点 Dock 里的「刷新」即可看到最新应用。", "PadLink",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("保存失败：" + ex.Message); }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer?.Dispose();
        base.OnFormClosed(e);
    }
}
