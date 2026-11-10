using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;
using Control = System.Windows.Controls.Control;
using ControlTemplate = System.Windows.Controls.ControlTemplate;
using FrameworkElementFactory = System.Windows.FrameworkElementFactory;
using TemplateBindingExtension = System.Windows.TemplateBindingExtension;
using Grid = System.Windows.Controls.Grid;
using Application = System.Windows.Application;

namespace PadLink.Wpf;

/// <summary>可编辑的应用条目。</summary>
public sealed class AppEntry
{
    public string Name { get; set; } = "";
    public string Cmd { get; set; } = "";
    public AppEntry() { }
    public AppEntry(string name, string cmd) { Name = name; Cmd = cmd; }
}

/// <summary>
/// 应用管理页：卡片式可编辑列表。
/// 每行 = 图标 + 名称输入 + 命令输入 + 删除按钮。增删行立即生效，点保存写入 apps.json。
/// </summary>
public sealed class AppsPageView : StackPanel
{
    private readonly List<AppEntry> _entries = new();
    private readonly StackPanel _rows = new();
    private readonly TextBlock _countLabel = new();
    private readonly Border _emptyBox;
    private readonly ScrollViewer _scroll;
    private bool _dirty;

    public AppsPageView()
    {
        Children.Add(Text.H1("应用管理"));
        Children.Add(Text.Sub(
            "手机端「应用」页会自动列出开始菜单中的所有程序；这里可补充自定义启动项，也可删除不需要的条目。"));

        var card = new Card();
        var inner = new StackPanel();

        // ---------- 表头 ----------
        inner.Children.Add(BuildHeaderRow());
        inner.Children.Add(new Border
        {
            Height = 1,
            Background = Tokens.Edge,
            Margin = new Thickness(0, 2, 0, 8),
        });

        // ---------- 行容器（可滚动） ----------
        _rows.Orientation = Orientation.Vertical;
        _scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 360,
        };
        inner.Children.Add(_scroll);

        // ---------- 空状态 ----------
        _emptyBox = new Border
        {
            Padding = new Thickness(0, 28, 0, 28),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = FluentIcons.Apps,
                        FontFamily = FluentIcons.Family,
                        FontSize = 26,
                        Foreground = Tokens.Ink3,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10),
                    },
                    new TextBlock
                    {
                        Text = "还没有自定义应用，点击下方「添加应用」开始",
                        FontSize = 12,
                        Foreground = Tokens.Ink3,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };
        inner.Children.Add(_emptyBox);

        // ---------- 操作栏 ----------
        var bar = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        for (int i = 0; i < 4; i++)
            bar.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto,
            });

        var addBtn = Buttons.PrimaryWithIcon(FluentIcons.Add, "添加应用", (s, e) => AddEntry(new AppEntry("", "")));
        addBtn.Margin = new Thickness(0, 0, 10, 0);

        var saveBtn = Buttons.GhostWithIcon(FluentIcons.Save, "保存", (s, e) => Save());
        saveBtn.Margin = new Thickness(0, 0, 10, 0);

        var reloadBtn = Buttons.GhostWithIcon(FluentIcons.Refresh, "重新载入", (s, e) => Reload());

        _countLabel.FontSize = 11.5;
        _countLabel.Foreground = Tokens.Ink3;
        _countLabel.VerticalAlignment = VerticalAlignment.Center;
        _countLabel.Margin = new Thickness(12, 0, 0, 0);

        Grid.SetColumn(addBtn, 0);
        Grid.SetColumn(saveBtn, 1);
        Grid.SetColumn(_countLabel, 2);
        Grid.SetColumn(reloadBtn, 3);
        bar.Children.Add(addBtn);
        bar.Children.Add(saveBtn);
        bar.Children.Add(_countLabel);
        bar.Children.Add(reloadBtn);
        inner.Children.Add(bar);

        card.Child = inner;
        Children.Add(card);

        // ---------- 说明卡片 ----------
        var tip = new Card();
        var tipStack = new StackPanel();
        tipStack.Children.Add(Text.Label("填写说明"));
        tipStack.Children.Add(new TextBlock
        {
            Text = "•  名称：手机端磁贴上显示的文字，如「微信」「VS Code」\n" +
                   "•  命令：完整路径（C:\\Program Files\\...\\app.exe）、PATH 程序名（notepad.exe）、\n" +
                   "     或 URI（ms-settings:、shell:AppsFolder\\...）\n" +
                   "•  名称或命令为空的行，保存时会自动跳过",
            FontSize = 11.5,
            Foreground = Tokens.Ink3,
            LineHeight = 21,
        });
        tip.Child = tipStack;
        Children.Add(tip);

        LoadFromDisk();
    }

    // ---------- 表头 ----------
    private static UIElement BuildHeaderRow()
    {
        var g = NewRowGrid();
        AddHeaderCell(g, "", 0);
        AddHeaderCell(g, "名称（手机显示）", 1, leftPad: 10);
        AddHeaderCell(g, "启动命令 / 路径 / URI", 2, leftPad: 10);
        AddHeaderCell(g, "", 3);
        return g;
    }

    private static Grid NewRowGrid()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        return g;
    }

    private static void AddHeaderCell(Grid g, string text, int col, double leftPad = 0)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Tokens.Ink3,
            Margin = new Thickness(leftPad, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ---------- 单行 ----------
    private UIElement BuildEntryRow(AppEntry entry)
    {
        var g = NewRowGrid();

        // 图标
        var iconBorder = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Tokens.CardAlt,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = FluentIcons.Rocket,
                FontFamily = FluentIcons.Family,
                FontSize = 12,
                Foreground = Tokens.AccentBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(iconBorder, 0);
        g.Children.Add(iconBorder);

        // 名称
        var nameBox = FlatBox("名称，如 微信", entry.Name);
        nameBox.Margin = new Thickness(0, 0, 8, 0);
        nameBox.TextChanged += (s, e) =>
        {
            entry.Name = nameBox.Text;
            MarkDirty();
        };
        Grid.SetColumn(nameBox, 1);
        g.Children.Add(nameBox);

        // 命令
        var cmdBox = FlatBox(@"notepad.exe 或 C:\...\app.exe", entry.Cmd);
        cmdBox.TextChanged += (s, e) =>
        {
            entry.Cmd = cmdBox.Text;
            MarkDirty();
        };
        Grid.SetColumn(cmdBox, 2);
        g.Children.Add(cmdBox);

        // 删除
        var del = new Button
        {
            Content = FluentIcons.Delete,
            FontFamily = FluentIcons.Family,
            FontSize = 13,
            Foreground = Tokens.Ink3,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Width = 32,
            Height = 30,
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = "删除此条",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Template = IconButtonTemplate(),
        };
        del.MouseEnter += (s, e) => del.Foreground = Tokens.DangerBrush;
        del.MouseLeave += (s, e) => del.Foreground = Tokens.Ink3;
        del.Click += (s, e) => RemoveEntry(entry);
        Grid.SetColumn(del, 3);
        g.Children.Add(del);

        return new Border
        {
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(8),
            Background = Brushes.Transparent,
            Child = g,
        };
    }

    private static TextBox FlatBox(string hint, string initial) => new()
    {
        Text = initial,
        ToolTip = hint,
        FontSize = 12.5,
        Foreground = Tokens.Ink,
        Background = Tokens.CardAlt,
        BorderThickness = new Thickness(1),
        BorderBrush = Tokens.Edge,
        Padding = new Thickness(9, 7, 9, 7),
        VerticalContentAlignment = VerticalAlignment.Center,
        Cursor = System.Windows.Input.Cursors.IBeam,
    };

    private static ControlTemplate IconButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        return t;
    }

    // ---------- 增删重绘 ----------
    private void AddEntry(AppEntry entry)
    {
        _entries.Add(entry);
        RebuildRows();
        MarkDirty();
        // 滚到新行
        Dispatcher.BeginInvoke(new Action(() => _scroll.ScrollToEnd()),
            DispatcherPriority.Background);
    }

    private void RemoveEntry(AppEntry entry)
    {
        _entries.Remove(entry);
        RebuildRows();
        MarkDirty();
    }

    private void RebuildRows()
    {
        _rows.Children.Clear();
        foreach (var e in _entries)
            _rows.Children.Add(BuildEntryRow(e));
        RefreshState();
    }

    // ---------- 数据 ----------
    private void LoadFromDisk()
    {
        _entries.Clear();
        try
        {
            foreach (dynamic a in AppLauncher.ListAppsRaw())
                _entries.Add(new AppEntry((string)a.name, (string)a.cmd));
        }
        catch (Exception ex)
        {
            DebugLog.Write("读取 apps.json 失败: " + ex.Message);
        }
        _dirty = false;
        RebuildRows();
    }

    private void Reload()
    {
        if (_dirty)
        {
            var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var r = owner != null
                ? MessageBox.Show(owner, "有未保存的修改，重新载入会丢弃，继续？", "PadLink",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBoxResult.Yes;
            if (r != MessageBoxResult.Yes) return;
        }
        LoadFromDisk();
    }

    private void Save()
    {
        try
        {
            var list = _entries
                .Select(e => (Name: (e.Name ?? "").Trim(), Cmd: (e.Cmd ?? "").Trim()))
                .Where(e => e.Name.Length > 0 && e.Cmd.Length > 0)
                .ToList();

            var skipped = _entries.Count - list.Count;
            AppLauncher.SaveApps(list);

            _entries.Clear();
            foreach (var (name, cmd) in list) _entries.Add(new AppEntry(name, cmd));
            _dirty = false;
            RebuildRows();

            var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var msg = $"已保存 {list.Count} 个应用。";
            if (skipped > 0) msg += $"\n（跳过 {skipped} 个名称为空或命令为空的行）";
            msg += "\n\n手机端打开「应用」页点刷新即可看到。";
            if (owner != null)
                MessageBox.Show(owner, msg, "PadLink", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
            var text = "保存失败：" + ex.Message;
            if (owner != null) MessageBox.Show(owner, text, "PadLink", MessageBoxButton.OK, MessageBoxImage.Error);
            else DebugLog.Write(text);
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        RefreshState();
    }

    private void RefreshState()
    {
        _countLabel.Text = _dirty
            ? $"共 {_entries.Count} 项 · 有未保存的修改"
            : $"共 {_entries.Count} 项";
        _countLabel.Foreground = _dirty ? Tokens.AccentBrush : Tokens.Ink3;

        bool empty = _entries.Count == 0;
        _emptyBox.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }
}
