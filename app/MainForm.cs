using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PadLink;

/// <summary>
/// PadLink 桌面控制台 —— 现代卡片式 UI：
/// 左侧窄导航（图标+标签），右侧内容区，卡片分组、圆角、深浅主题跟随系统。
/// </summary>
internal sealed class MainForm : Form
{
    // ---- 设计令牌（深色） ----
    private static readonly Color Bg = Color.FromArgb(13, 14, 18);
    private static readonly Color Nav = Color.FromArgb(18, 19, 25);
    private static readonly Color Card = Color.FromArgb(21, 23, 29);
    private static readonly Color CardAlt = Color.FromArgb(27, 30, 38);
    private static readonly Color Edge = Color.FromArgb(37, 41, 53);
    private static readonly Color Ink = Color.FromArgb(233, 234, 239);
    private static readonly Color Ink2 = Color.FromArgb(154, 161, 173);
    private static readonly Color Ink3 = Color.FromArgb(107, 114, 128);
    private static readonly Color Accent = Color.FromArgb(76, 134, 255);
    private static readonly Color Ok = Color.FromArgb(52, 211, 153);
    private static readonly Color Danger = Color.FromArgb(248, 113, 113);

    private readonly Panel _content = new();
    private readonly Panel _navBar = new();
    private readonly List<(Button Btn, Panel Page)> _tabs = new();
    private readonly Label _statusLabel = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1500 };

    private readonly Panel _pageConn, _pageApps, _pageSettings;

    public MainForm()
    {
        Text = "PadLink";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(880, 580);
        MinimumSize = new Size(760, 520);
        BackColor = Bg;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        BuildNav();

        _pageConn = BuildConnPage();
        _pageApps = BuildAppsPage();
        _pageSettings = BuildSettingsPage();
        foreach (var p in new[] { _pageConn, _pageApps, _pageSettings })
        {
            p.Dock = DockStyle.Fill;
            p.Visible = false;
            _content.Controls.Add(p);
        }
        _content.Dock = DockStyle.Fill;
        _content.BackColor = Bg;
        _content.Padding = new Padding(26, 18, 26, 20);

        Controls.Add(_content);
        Controls.Add(_navBar);
        SelectTab(0);

        _timer.Tick += (s, e) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    // ================= 导航 =================
    private void BuildNav()
    {
        _navBar.Dock = DockStyle.Left;
        _navBar.Width = 172;
        _navBar.BackColor = Nav;
        _navBar.Padding = new Padding(12, 22, 12, 16);

        var brand = new Label
        {
            Text = "PadLink",
            Font = new Font("Segoe UI Semibold", 14.5f, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = false,
            Height = 46,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _navBar.Controls.Add(brand);

        var items = new[] { ("◉", "概览"), ("▦", "应用管理"), ("⚙", "设置") };
        var pages = new List<Button>();
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var (glyph, label) = items[i];
            var b = new Button
            {
                Text = $"  {glyph}   {label}",
                TextAlign = ContentAlignment.MiddleLeft,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(148, 42),
                Location = new Point(12, 74 + i * 48),
                Font = new Font("Segoe UI", 10.5f),
                ForeColor = Ink2,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => SelectTab(idx);
            _navBar.Controls.Add(b);
            pages.Add(b);
        }

        // 用字段保存后建页时回填
        _navButtons = pages;
    }

    private List<Button> _navButtons = new();
    private Panel[] _pageList = Array.Empty<Panel>();

    private void SelectTab(int index)
    {
        if (_pageList.Length == 0) _pageList = new[] { _pageConn, _pageApps, _pageSettings };
        for (int i = 0; i < _pageList.Length; i++)
        {
            _pageList[i].Visible = i == index;
            var b = _navButtons[i];
            bool on = i == index;
            b.BackColor = on ? Color.FromArgb(32, 38, 54) : Color.Transparent;
            b.ForeColor = on ? Accent : Ink2;
            b.FlatAppearance.BorderSize = 0;
        }
    }

    // ================= 通用控件 =================
    private static Label Title(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
        ForeColor = Ink,
        AutoSize = true,
        Margin = new Padding(0, 0, 0, 4),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = Ink3,
        AutoSize = true,
        MaximumSize = new Size(560, 0),
        Margin = new Padding(0, 0, 0, 0),
    };

    private static Label SectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
        ForeColor = Ink3,
        AutoSize = true,
        Margin = new Padding(2, 12, 0, 6),
    };

    /// <summary>圆角卡片容器。</summary>
    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            BackColor = Card;
            Padding = new Padding(18, 15, 18, 15);
            DoubleBuffered = true;
            ResizeRedraw = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Edge, 1f);
            using var path = Round(new Rectangle(0, 0, Width - 1, Height - 1), 14);
            g.DrawPath(pen, path);
        }
        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    private static Button PrimaryButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = Accent,
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
        Size = new Size(150, 44),
        AutoSize = false,
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    private static Button GhostButton(string text, Color? fore = null) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = CardAlt,
        ForeColor = fore ?? Ink,
        Font = new Font("Segoe UI", 10f),
        Size = new Size(140, 44),
        AutoSize = false,
        Cursor = Cursors.Hand,
        FlatAppearance = { BorderSize = 0 },
    };

    private static Label ValueLabel(string text, Color? color = null) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
        ForeColor = color ?? Ink,
        AutoSize = true,
    };

    // ================= 概览页 =================
    private Panel BuildConnPage()
    {
        var page = new Panel { BackColor = Bg };
        var flow = NewFlow();

        flow.Controls.Add(Title("概览"));
        flow.Controls.Add(Caption("手机与电脑需处于同一 WiFi，浏览器或 App 连接下方地址。"));

        // 连接卡片
        var c1 = new CardPanel { Dock = DockStyle.Top, Height = 168, Margin = new Padding(0, 14, 0, 0) };
        var l1 = NewCardFlow();
        l1.Controls.Add(SectionLabel("连接地址"));
        var url = new Label
        {
            Text = TrayContext.BaseUrl,
            Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize = true,
        };
        l1.Controls.Add(url);
        _statusLabel.Font = new Font("Segoe UI", 9.5f);
        _statusLabel.ForeColor = Ink3;
        _statusLabel.AutoSize = true;
        l1.Controls.Add(_statusLabel);

        var btnRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0, 10, 0, 0) };
        var open = PrimaryButton("打开控制页");
        open.Click += (s, e) => Process.Start(new ProcessStartInfo(TrayContext.BaseUrl) { UseShellExecute = true });
        var copy = GhostButton("复制地址");
        copy.Click += (s, e) => { Clipboard.SetText(TrayContext.BaseUrl); _statusLabel.Text = "已复制到剪贴板 ✓"; };
        btnRow.Controls.Add(open);
        btnRow.Controls.Add(copy);
        l1.Controls.Add(btnRow);
        c1.Controls.Add(l1);
        flow.Controls.Add(c1);

        // 使用步骤卡片
        var c2 = new CardPanel { Dock = DockStyle.Top, Height = 148, Margin = new Padding(0, 12, 0, 0) };
        var l2 = NewCardFlow();
        l2.Controls.Add(SectionLabel("使用步骤"));
        l2.Controls.Add(Caption("1.  手机连接同一 WiFi\n2.  浏览器打开上方地址，或安装 PadLink App\n3.  iPhone：分享 → 添加到主屏幕，获得全屏体验\n4.  连接一次后会记住地址，下次自动重连"));
        c2.Controls.Add(l2);
        flow.Controls.Add(c2);

        page.Controls.Add(flow);
        return page;
    }

    private void RefreshStatus()
    {
        if (_statusLabel.IsDisposed) return;
        int n = WebSocketHandler.ConnectedCount;
        _statusLabel.Text = n > 0
            ? $"● 已连接 {n} 台设备    局域网 IP：{TrayContext.LocalIP}"
            : $"○ 等待连接    局域网 IP：{TrayContext.LocalIP}";
        _statusLabel.ForeColor = n > 0 ? Ok : Ink3;
    }

    // ================= 应用管理页 =================
    private DataGridView? _grid;

    private Panel BuildAppsPage()
    {
        var page = new Panel { BackColor = Bg };
        var flow = NewFlow();

        flow.Controls.Add(Title("应用管理"));
        flow.Controls.Add(Caption("手机应用页会显示开始菜单里的所有程序，这里可以额外固定常用项（可选）。"));

        var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 14, 0, 0) };
        var inner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inner.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Card,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AllowUserToAddRows = true,
            AllowUserToDeleteRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            EnableHeadersVisualStyles = false,
            GridColor = Edge,
            RowTemplate = { Height = 34 },
            ColumnHeadersHeight = 36,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = CardAlt;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = CardAlt;
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor = Card;
        _grid.DefaultCellStyle.ForeColor = Ink;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(44, 52, 70);
        _grid.DefaultCellStyle.SelectionForeColor = Ink;
        _grid.Columns.Add("name", "名称（手机显示）");
        _grid.Columns.Add("cmd", "启动命令 / 路径 / URI");
        LoadApps();

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0),
        };
        var save = PrimaryButton("保存");
        save.Width = 110;
        save.Click += (s, e) => SaveApps();
        var add = GhostButton("添加一行");
        add.Width = 110;
        add.Click += (s, e) => _grid.Rows.Add("", "");
        var del = GhostButton("删除选中");
        del.Width = 110;
        del.Click += (s, e) =>
        {
            foreach (DataGridViewRow r in _grid.SelectedRows)
                if (!r.IsNewRow) _grid.Rows.Remove(r);
        };
        bar.Controls.Add(save);
        bar.Controls.Add(add);
        bar.Controls.Add(del);

        inner.Controls.Add(_grid, 0, 0);
        inner.Controls.Add(bar, 0, 1);
        card.Controls.Add(inner);
        flow.Controls.Add(card);

        page.Controls.Add(flow);
        return page;
    }

    private void LoadApps()
    {
        if (_grid == null) return;
        _grid.Rows.Clear();
        foreach (dynamic a in AppLauncher.ListAppsRaw())
            _grid.Rows.Add(a.name, a.cmd);
        if (_grid.Rows.Count == 0) _grid.Rows.Add("", "");
    }

    private void SaveApps()
    {
        if (_grid == null) return;
        try
        {
            var list = new List<(string, string)>();
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (r.IsNewRow) continue;
                var n = Convert.ToString(r.Cells[0].Value)?.Trim() ?? "";
                var c = Convert.ToString(r.Cells[1].Value)?.Trim() ?? "";
                if (n.Length > 0 && c.Length > 0) list.Add((n, c));
            }
            AppLauncher.SaveApps(list);
            MessageBox.Show(this, "已保存。手机端打开「应用」页刷新即可看到。", "PadLink",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "PadLink",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ================= 设置页 =================
    private Panel BuildSettingsPage()
    {
        var page = new Panel { BackColor = Bg };
        var flow = NewFlow();
        flow.Controls.Add(Title("设置"));
        flow.Controls.Add(Caption("这些选项保存在当前用户下，无需管理员权限。"));

        var card = new CardPanel { Dock = DockStyle.Top, Height = 210, Margin = new Padding(0, 14, 0, 0) };
        var inner = NewCardFlow();
        inner.Controls.Add(SectionLabel("启动"));

        var autoStart = new CheckBox
        {
            Text = "开机自动启动 PadLink",
            ForeColor = Ink,
            AutoSize = true,
            Checked = Autostart.IsEnabled(),
            Margin = new Padding(2, 4, 0, 4),
        };
        autoStart.CheckedChanged += (s, e) =>
        {
            if (Autostart.IsEnabled() != autoStart.Checked) Autostart.Toggle();
        };
        inner.Controls.Add(autoStart);

        var autoPage = new CheckBox
        {
            Text = "启动时自动打开控制页（默认关闭）",
            ForeColor = Ink,
            AutoSize = true,
            Checked = AppSettings.AutoOpenPage,
            Margin = new Padding(2, 4, 0, 10),
        };
        autoPage.CheckedChanged += (s, e) => AppSettings.AutoOpenPage = autoPage.Checked;
        inner.Controls.Add(autoPage);

        inner.Controls.Add(SectionLabel("维护"));
        var btnRow = new FlowLayoutPanel { AutoSize = true, BackColor = Color.Transparent };
        var logBtn = GhostButton("打开程序目录");
        logBtn.Click += (s, e) => Process.Start("explorer.exe", AppContext.BaseDirectory);
        var restartBtn = GhostButton("重启服务");
        restartBtn.Click += (s, e) =>
        {
            var r = MessageBox.Show(this, "重启服务会短暂断开所有已连接设备，继续？", "PadLink",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes)
                MessageBox.Show(this, "请退出托盘程序后重新运行即可（当前版本未提供热重启）。", "PadLink");
        };
        var exitBtn = GhostButton("退出 PadLink", Danger);
        exitBtn.Click += (s, e) =>
        {
            var r = MessageBox.Show(this, "退出后手机将无法连接，确定？", "PadLink",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) Close();
        };
        btnRow.Controls.Add(logBtn);
        btnRow.Controls.Add(restartBtn);
        btnRow.Controls.Add(exitBtn);
        inner.Controls.Add(btnRow);
        card.Controls.Add(inner);
        flow.Controls.Add(card);

        var about = new CardPanel { Dock = DockStyle.Top, Height = 118, Margin = new Padding(0, 12, 0, 0) };
        var aboutInner = NewCardFlow();
        aboutInner.Controls.Add(SectionLabel("关于"));
        aboutInner.Controls.Add(Caption($"PadLink 1.2  ·  .NET 8 / WinForms\n" +
            $"服务端口 {TrayContext.Port}  ·  协议 WebSocket 明文，仅限可信局域网\n" +
            "手机端支持浏览器（PWA）与原生安卓 App"));
        about.Controls.Add(aboutInner);
        flow.Controls.Add(about);

        page.Controls.Add(flow);
        return page;
    }

    // ================= 布局辅助 =================
    private static FlowLayoutPanel NewFlow() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        BackColor = Bg,
    };

    private static FlowLayoutPanel NewCardFlow() => new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = false,
        BackColor = Card,
    };

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
