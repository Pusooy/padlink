using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PadLink.App.Services;
using PadLink.App.ViewModels;

namespace PadLink.App.Views;

/// <summary>概览页「手机端可用能力」条目。</summary>
public sealed class CapabilityItem
{
    public string Glyph { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
}

public sealed partial class OverviewPage : Page
{
    /// <summary>
    /// 宽/窄布局切换阈值，按窗口宽度计算（页面宽度 + NavigationView 侧栏宽度）。
    /// 1000px 窗口在 248px 侧栏下仍有约 750px 内容区，足够放下两列。
    /// </summary>
    private const double WideLayoutThreshold = 1000;

    private bool? _isWide;

    public ShellViewModel Vm { get; private set; } = null!;

    /// <summary>能力列表数据源（ItemsRepeater 流式布局）。</summary>
    public IReadOnlyList<CapabilityItem> Capabilities { get; } = new List<CapabilityItem>
    {
        new() { Glyph = "\uE962", Title = "触控板 + 手势", Description = "单指移动 / 轻点左键 / 双指右键 / 滑动滚动" },
        new() { Glyph = "\uE765", Title = "86 键键盘 + 语音", Description = "完整键盘布局，支持语音转文字直输" },
        new() { Glyph = "\uE768", Title = "媒体与音量", Description = "播放控制、主机音量滑杆、系统快捷键" },
        new() { Glyph = "\uE71D", Title = "应用与窗口", Description = "实时窗口切换/关闭，程序搜索启动" },
        new() { Glyph = "\uE7F4", Title = "屏幕镜像", Description = "查看电脑画面，点按即定位点击" },
        new() { Glyph = "\uE8B7", Title = "文件管理", Description = "浏览/预览/上传/下载电脑文件" },
        new() { Glyph = "\uE8C8", Title = "剪贴板同步", Description = "手机与电脑剪贴板双向互通" },
        new() { Glyph = "\uE7E8", Title = "电源控制", Description = "锁定、睡眠、注销、重启、关机" },
    };

    public OverviewPage()
    {
        InitializeComponent();
        SizeChanged += OnPageSizeChanged;
        HeroCard.SizeChanged += (_, _) => UpdateHeroLayout();
        Loaded += (_, _) => UpdateContentHeight();
        PageScroll.SizeChanged += (_, _) => UpdateContentHeight();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ShellViewModel vm)
        {
            Vm = vm;
            DataContext = vm;
            RenderQrCode();
        }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayoutState(WindowWidth(e.NewSize.Width));
        UpdateHeroLayout();
        UpdateContentHeight();
    }

    /// <summary>
    /// 取窗口可用宽度（不是页面宽度）：XamlRoot.Size 是窗口客户区尺寸，
    /// 不受 NavigationView 侧栏是否展开影响，比按侧栏宽度反推更可靠。
    /// </summary>
    private double WindowWidth(double pageWidth)
    {
        double rootWidth = XamlRoot?.Size.Width ?? 0;
        return rootWidth > 0 ? rootWidth : pageWidth;
    }

    /// <summary>
    /// 宽屏时让首行撑满视口剩余高度，消除底部空白。
    /// ScrollViewer 以无限高度测量内容，星号行会退化为内容高度，
    /// 因此按视口高度算出首行应有高度并显式设置（内容更多时自动变高，不裁切）。
    /// </summary>
    private void UpdateContentHeight()
    {
        if (HeroCard is null || PageScroll is null || PageHeader is null || CapabilitiesCard is null)
            return;

        // 窄屏为单列纵排，允许滚动，不做拉伸
        if (_isWide != true)
        {
            HeroCard.MinHeight = 0;
            return;
        }

        double viewport = PageScroll.ViewportHeight;
        if (double.IsNaN(viewport) || viewport <= 0) return;

        // 视口 - 上下外边距(16+24) - 页头 - 能力卡片 - 两处 16px 行间距
        double target = viewport - 40
                        - PageHeader.ActualHeight
                        - CapabilitiesCard.ActualHeight
                        - 32;

        target = Math.Max(0, target);
        if (Math.Abs(HeroCard.MinHeight - target) > 0.5)
            HeroCard.MinHeight = target;
    }

    /// <summary>按可用宽度在宽/窄两套布局间切换（两列 ↔ 单列）。</summary>
    private void ApplyLayoutState(double width)
    {
        bool wide = width >= WideLayoutThreshold;
        if (_isWide == wide) return;
        _isWide = wide;

        if (wide)
        {
            // 两列：左 5 / 右 3，右栏与连接卡片同处第一行
            PrimaryColumn.Width = new GridLength(5, GridUnitType.Star);
            SecondaryColumn.Width = new GridLength(3, GridUnitType.Star);
            HeroRow.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetColumnSpan(HeroCard, 1);
            Grid.SetRow(RightRail, 1);
            Grid.SetColumn(RightRail, 1);
            Grid.SetColumnSpan(RightRail, 1);
            Grid.SetRow(CapabilitiesCard, 2);

            CapabilityList.MinItemWidth = 248;
        }
        else
        {
            // 单列：全部堆叠在一列内，允许纵向滚动
            PrimaryColumn.Width = new GridLength(1, GridUnitType.Star);
            SecondaryColumn.Width = new GridLength(0, GridUnitType.Pixel);
            HeroRow.Height = GridLength.Auto;

            Grid.SetColumnSpan(HeroCard, 2);
            Grid.SetRow(RightRail, 2);
            Grid.SetColumn(RightRail, 0);
            Grid.SetColumnSpan(RightRail, 2);
            Grid.SetRow(CapabilitiesCard, 3);

            CapabilityList.MinItemWidth = 280;
        }

        UpdateQrSize(wide);
        UpdateHeroLayout();
        UpdateContentHeight();
    }

    /// <summary>
    /// 连接卡片内部再响应一次：优先让地址与二维码并排（地址是主角），
    /// 从大到小尝试字号；都放不下时才把二维码移到地址下方。
    /// 宽度用临时 TextBlock 实测，避免按字符数估算带来的误差。
    /// </summary>
    private void UpdateHeroLayout()
    {
        if (HeroCard is null || QrPanel is null || UrlText is null) return;
        if (string.IsNullOrEmpty(Vm?.Url)) return;

        const double CardPadding = 48;   // 卡片左右各 24
        const double ColumnSpacing = 24;
        double available = HeroCard.ActualWidth - QrPanel.Width - ColumnSpacing - CardPadding;
        if (available <= 0) return;

        foreach (double fontSize in new[] { 34.0, 32.0, 30.0, 28.0, 26.0, 24.0 })
        {
            if (MeasureUrlWidth(fontSize) <= available)
            {
                // 并排：二维码占右侧，纵向与地址块居中对齐
                Grid.SetRow(QrPanel, 0);
                Grid.SetColumn(QrPanel, 1);
                Grid.SetRowSpan(QrPanel, 2);
                QrPanel.HorizontalAlignment = HorizontalAlignment.Right;
                UrlText.FontSize = fontSize;
                UrlText.TextWrapping = TextWrapping.NoWrap;
                return;
            }
        }

        // 并排放不下：二维码移到地址下方，地址用小字号并允许换行
        Grid.SetRow(QrPanel, 1);
        Grid.SetColumn(QrPanel, 0);
        Grid.SetRowSpan(QrPanel, 1);
        QrPanel.HorizontalAlignment = HorizontalAlignment.Left;
        UrlText.FontSize = 26;
        UrlText.TextWrapping = TextWrapping.Wrap;
    }

    /// <summary>实测 URL 在指定字号下的渲染宽度（像素）。</summary>
    private double MeasureUrlWidth(double fontSize)
    {
        var probe = new TextBlock
        {
            Text = Vm?.Url ?? "",
            FontSize = fontSize,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
        };
        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>窄屏时二维码略小，宽屏时保持清晰可扫的尺寸。</summary>
    private void UpdateQrSize(bool wide)
    {
        if (QrImageControl == null || QrPanel == null) return;
        double size = wide ? 162 : 150;
        QrImageControl.Width = size;
        QrImageControl.Height = size;
        QrPanel.Width = size + 22;
        RenderQrCode();
    }

    /// <summary>把当前连接地址渲染为二维码位图；无地址或编码失败时降级为提示文字。</summary>
    private void RenderQrCode()
    {
        if (Vm == null || QrImageControl == null) return;

        try
        {
            // 按 2 倍分辨率渲染，缩放到控件尺寸时保持模块边缘锐利
            double logical = QrImageControl.Width > 0 ? QrImageControl.Width : 162;
            int pixels = (int)(logical * 2);
            // 深色接近纯黑、浅色纯白：不随主题反色，确保手机稳定识别
            var bitmap = QrImage.Create(
                Vm.Url,
                pixels,
                Windows.UI.Color.FromArgb(255, 0x1A, 0x1A, 0x1A),
                Windows.UI.Color.FromArgb(255, 0xFF, 0xFF, 0xFF));

            QrImageControl.Source = bitmap;
            QrImageControl.Visibility = bitmap != null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // 渲染失败不应影响页面可用性，静默降级
            QrImageControl.Source = null;
            QrImageControl.Visibility = Visibility.Collapsed;
        }
    }
}
