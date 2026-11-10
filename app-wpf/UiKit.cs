using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using VerticalAlignment = System.Windows.VerticalAlignment;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using FrameworkElement = System.Windows.FrameworkElement;
using Control = System.Windows.Controls.Control;
using TemplateBindingExtension = System.Windows.TemplateBindingExtension;
using ControlTemplate = System.Windows.Controls.ControlTemplate;
using FrameworkElementFactory = System.Windows.FrameworkElementFactory;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

namespace PadLink.Wpf;

/// <summary>设计令牌（Fluent 风格，跟随系统明暗）。</summary>
public static class Tokens
{
    public static bool Dark { get; private set; } = true;

    public static readonly Color Accent = Color.FromRgb(0x4C, 0x86, 0xFF);
    public static readonly Brush AccentBrush = Frozen(Accent);
    public static readonly Brush AccentSoft = Frozen(Color.FromArgb(0x30, 0x4C, 0x86, 0xFF));
    public static readonly Brush OkBrush = Frozen(Color.FromRgb(0x34, 0xD3, 0x99));
    public static readonly Brush DangerBrush = Frozen(Color.FromRgb(0xF8, 0x71, 0x71));

    public static Brush Bg => Frozen(Dark ? Color.FromRgb(0x0D, 0x0E, 0x12) : Color.FromRgb(0xF4, 0xF5, 0xF7));
    public static Brush NavBg => Frozen(Dark ? Color.FromRgb(0x12, 0x13, 0x19) : Color.FromRgb(0xEA, 0xEC, 0xF0));
    public static Brush CardBg => Frozen(Dark ? Color.FromRgb(0x17, 0x19, 0x21) : Color.FromRgb(0xFF, 0xFF, 0xFF));
    public static Brush CardAlt => Frozen(Dark ? Color.FromRgb(0x1E, 0x21, 0x2B) : Color.FromRgb(0xF2, 0xF4, 0xF8));
    public static Brush Edge => Frozen(Dark ? Color.FromRgb(0x27, 0x2B, 0x37) : Color.FromRgb(0xE2, 0xE5, 0xEB));
    public static Brush Ink => Frozen(Dark ? Color.FromRgb(0xE9, 0xEA, 0xEF) : Color.FromRgb(0x15, 0x17, 0x1C));
    public static Brush Ink2 => Frozen(Dark ? Color.FromRgb(0x9A, 0xA1, 0xAD) : Color.FromRgb(0x6B, 0x72, 0x80));
    public static Brush Ink3 => Frozen(Dark ? Color.FromRgb(0x6B, 0x72, 0x80) : Color.FromRgb(0x9A, 0xA0, 0xAB));

    public static void SetTheme(bool dark) => Dark = dark;

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

/// <summary>圆角卡片：带 1px 描边和柔和阴影。</summary>
public sealed class Card : Border
{
    public Card()
    {
        Background = Tokens.CardBg;
        BorderBrush = Tokens.Edge;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(20, 18, 20, 18);
        Margin = new Thickness(0, 0, 0, 14);
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 2,
            Opacity = 0.10,
            Color = Colors.Black,
        };
    }
}

/// <summary>设置行：左侧标题/说明，右侧控件。</summary>
public sealed class SettingRow : Grid
{
    public SettingRow(string title, string? subtitle, UIElement control)
    {
        Margin = new Thickness(0, 7, 0, 7);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            Foreground = Tokens.Ink,
        });
        if (!string.IsNullOrEmpty(subtitle))
            left.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 11,
                Foreground = Tokens.Ink3,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        Children.Add(left);

        ((FrameworkElement)control).VerticalAlignment = VerticalAlignment.Center;
        SetColumn(control, 1);
        Children.Add(control);
    }
}

/// <summary>Fluent 风格开关。</summary>
public sealed class ToggleSwitch : Grid
{
    private readonly Border _track;
    private readonly Ellipse _thumb;
    private bool _on;

    public event EventHandler<bool>? Toggled;

    public bool IsOn
    {
        get => _on;
        set
        {
            _on = value;
            Animate();
            Toggled?.Invoke(this, value);
        }
    }

    public ToggleSwitch(bool initial = false)
    {
        Width = 46; Height = 26;
        Cursor = System.Windows.Input.Cursors.Hand;

        _track = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = initial ? Tokens.AccentBrush : Tokens.CardAlt,
            BorderBrush = Tokens.Edge,
            BorderThickness = new Thickness(1),
        };
        _thumb = new Ellipse
        {
            Width = 18, Height = 18,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
        };
        Children.Add(_track);
        Children.Add(_thumb);
        _on = initial;
        MouseLeftButtonUp += (s, e) => IsOn = !IsOn;
        Update();
    }

    private void Animate()
    {
        Update();
        var target = _on ? 22d : 3d;
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        _thumb.BeginAnimation(MarginProperty, null);
        _thumb.Margin = new Thickness(target, 0, 0, 0);
    }

    private void Update()
    {
        _track.Background = _on ? Tokens.AccentBrush : Tokens.CardAlt;
        _thumb.Margin = new Thickness(_on ? 22 : 3, 0, 0, 0);
    }
}

/// <summary>可选中的分段按钮组。</summary>
public sealed class SegmentedControl : Border
{
    private readonly List<Button> _buttons = new();
    public event EventHandler<int>? Changed;
    private int _selected;

    public int Value => _selected;

    public SegmentedControl(string[] labels, int selected = 0)
    {
        _selected = selected;
        Background = Tokens.CardAlt;
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(3);
        BorderBrush = Tokens.Edge;
        BorderThickness = new Thickness(1);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            var b = new Button
            {
                Content = labels[i],
                FontSize = 12,
                Padding = new Thickness(14, 5, 14, 5),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Tokens.Ink2,
                Cursor = System.Windows.Input.Cursors.Hand,
                Template = RoundButtonTemplate(),
            };
            b.Click += (s, e) => { SetSelected(idx); Changed?.Invoke(this, idx); };
            _buttons.Add(b);
            panel.Children.Add(b);
        }
        Child = panel;
        Refresh();
    }

    private void SetSelected(int value)
    {
        _selected = value;
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == _selected;
            _buttons[i].Background = on ? Tokens.CardBg : Brushes.Transparent;
            _buttons[i].Foreground = on ? Tokens.AccentBrush : Tokens.Ink2;
        }
    }

    private static ControlTemplate RoundButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        return t;
    }
}

/// <summary>主/次要按钮。</summary>
public static class Buttons
{
    public static Button Primary(string text, RoutedEventHandler onClick)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 9, 18, 9),
            Foreground = Brushes.White,
            Background = Tokens.AccentBrush,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Rounded(),
        };
        b.Click += onClick;
        return b;
    }

    public static Button Ghost(string text, RoutedEventHandler onClick, Brush? fore = null)
    {
        var b = new Button
        {
            Content = text,
            FontSize = 12.5,
            Padding = new Thickness(18, 9, 18, 9),
            Foreground = fore ?? Tokens.Ink,
            Background = Tokens.CardAlt,
            BorderBrush = Tokens.Edge,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = Rounded(),
        };
        b.Click += onClick;
        return b;
    }

    /// <summary>带 Fluent 图标的按钮内容（图标 + 文字横排）。</summary>
    private static StackPanel IconContent(string glyph, string text, Brush foreground)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = FluentIcons.Family,
            FontSize = 14,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        });
        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    public static Button PrimaryWithIcon(string glyph, string text, RoutedEventHandler onClick)
    {
        var b = Primary(text, onClick);
        b.Content = IconContent(glyph, text, Brushes.White);
        return b;
    }

    public static Button GhostWithIcon(string glyph, string text, RoutedEventHandler onClick,
                                       Brush? fore = null)
    {
        var fg = fore ?? Tokens.Ink;
        var b = Ghost(text, onClick, fore);
        b.Content = IconContent(glyph, text, fg);
        return b;
    }

    private static ControlTemplate Rounded()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        t.VisualTree = border;
        return t;
    }
}

public static class Text
{
    public static TextBlock H1(string s) => new()
    {
        Text = s, FontSize = 21, FontWeight = FontWeights.Bold,
        Foreground = Tokens.Ink, Margin = new Thickness(0, 0, 0, 4),
    };

    public static TextBlock Sub(string s) => new()
    {
        Text = s, FontSize = 12, Foreground = Tokens.Ink3,
        Margin = new Thickness(0, 0, 0, 14), TextWrapping = TextWrapping.Wrap,
    };

    public static TextBlock Label(string s) => new()
    {
        Text = s.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold,
        Foreground = Tokens.Ink3, Margin = new Thickness(0, 12, 0, 8),
    };

    public static TextBlock Mono(string s) => new()
    {
        Text = s, FontFamily = new FontFamily("Consolas"), FontSize = 13,
        Foreground = Tokens.Ink2, TextWrapping = TextWrapping.Wrap,
    };
}

/// <summary>
/// Fluent 图标：使用 Windows 11 自带的 Segoe Fluent Icons 字体。
/// 相比文字符号（◉ ▦ ⚙）是真正的矢量图标，与系统设置界面一致。
/// </summary>
public static class FluentIcons
{
    /// <summary>字体实例：优先 Segoe Fluent Icons（Win11），缺失时回退 Segoe MDL2 Assets（Win10）。</summary>
    public static readonly FontFamily Family = ResolveIconFont();

    private static FontFamily ResolveIconFont()
    {
        // 逐一尝试，返回第一个在系统中真实存在的字体
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                var ff = new FontFamily(name);
                // 用 GetTypefaces 探测是否可用（不存在时返回空集合）
                var typefaces = ff.GetTypefaces();
                if (typefaces != null && typefaces.Count > 0) return ff;
            }
            catch { }
        }
        return new FontFamily("Segoe UI");
    }

    // ---- 常用字形（Win11 / Win10 MDL2 通用码点）----
    public const string Home = "\uE80F";            // 主页
    public const string Settings = "\uE713";        // 齿轮
    public const string Apps = "\uE71D";            // 应用网格
    public const string MousePointer = "\uE962";    // 指针
    public const string Keyboard = "\uE765";        // 键盘
    public const string Play = "\uE768";            // 播放
    public const string Screen = "\uE7F4";          // 显示器
    public const string Folder = "\uE8B7";          // 文件夹
    public const string Copy = "\uE8C8";            // 剪贴板
    public const string Power = "\uE7E8";           // 电源
    public const string Link = "\uE71B";            // 链接
    public const string Refresh = "\uE72C";         // 刷新
    public const string Info = "\uE946";            // 信息
    public const string ChevronRight = "\uE76C";    // 右箭头
    public const string ChevronLeft = "\uE76B";
    public const string Check = "\uE73E";           // 对勾
    public const string Copy2 = "\uE8C8";
    public const string QRCode = "\uED14";          // 二维码
    public const string FolderOpen = "\uE838";      // 打开文件夹
    public const string Document = "\uE8A5";        // 文档
    public const string Log = "\uE9D9";             // 日志/条目
    public const string Exit = "\uE8BB";            // 退出
    public const string Rocket = "\uE82D";          // 启动/发射
    public const string Add = "\uE710";             // 加号
    public const string Save = "\uE74E";            // 保存
    public const string Delete = "\uE74D";          // 删除（垃圾桶）
    public const string Edit = "\uE70F";            // 编辑
    public const string Warning = "\uE7BA";         // 警告
    public const string Globe = "\uE774";           // 网页


    /// <summary>创建一个 Fluent 图标 TextBlock。</summary>
    public static TextBlock Icon(string glyph, double size = 16, Brush? color = null) => new()
    {
        Text = glyph,
        FontFamily = Family,
        FontSize = size,
        Foreground = color ?? Tokens.Ink2,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };
}
