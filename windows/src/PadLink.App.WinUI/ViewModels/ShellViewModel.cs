using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using PadLink.Core;
using PadLink.Interop;

namespace PadLink.App.ViewModels;

/// <summary>Shell 视图模型：承载连接状态、设置、应用列表等全局状态。</summary>
public partial class ShellViewModel : ObservableObject
{
    private readonly PadLinkSettings _settings;
    private readonly DispatcherQueueTimer _timer;

    [ObservableProperty] private string _url = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IpLine))]
    private string _localIp = "";
    [ObservableProperty] private string _statusText = "等待连接";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectedCountText))]
    private int _connectedCount;
    [ObservableProperty] private Microsoft.UI.Xaml.Media.Brush _statusBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    [ObservableProperty] private PadLinkSettings _settingsRef;

    public ObservableCollection<AppEntryModel> Apps { get; } = new();

    /// <summary>连接数量文案，供概览页「实时状态」卡片直接绑定。</summary>
    public string ConnectedCountText => ConnectedCount > 0
        ? $"{ConnectedCount} 台设备已连接"
        : "暂无设备连接";

    /// <summary>端口号，供概览页「实时状态」卡片展示。</summary>
    public string PortText => _settings.Port.ToString();

    /// <summary>局域网 IP + 端口的一行摘要。</summary>
    public string IpLine => $"局域网 IP {LocalIp} · 端口 {PortText}";

    public ShellViewModel(PadLinkSettings settings)
    {
        _settings = settings;
        SettingsRef = settings;
        LocalIp = PadLink.App.Services.NetworkInfo.LocalIP;
        Url = $"http://{LocalIp}:{settings.Port}";

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1.2);
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();

        LoadApps();
    }

    private void RefreshStatus()
    {
        var n = PadLink.Interop.WebSocketHandler.ConnectedCount;
        ConnectedCount = n;
        IsConnected = n > 0;
        StatusText = n > 0 ? $"已连接 {n} 台设备" : "等待手机连接";
        StatusBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            n > 0 ? Microsoft.UI.ColorHelper.FromArgb(255, 0x34, 0xD3, 0x99)
                  : Microsoft.UI.ColorHelper.FromArgb(255, 0x6B, 0x72, 0x80));
    }

    public void LoadApps()
    {
        Apps.Clear();
        foreach (var a in AppsStore.Load()) Apps.Add(a);
    }

    public void SaveApps()
    {
        AppsStore.Save(Apps.Where(a =>
            !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Cmd)));
        Log.Info($"已保存 {Apps.Count} 个应用");
        LoadApps();
    }

    [RelayCommand]
    private void OpenControlPage()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Log.Error("打开控制页失败", ex); }
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            var dp = new System.Windows.Forms.DataObject();
            dp.SetText(Url);
            System.Windows.Forms.Clipboard.SetDataObject(dp, true, 5, 100);
            StatusText = "地址已复制";
        }
        catch (Exception ex) { Log.Error("复制失败", ex); }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SettingsStore.Save(_settings);
        StatusText = "设置已保存";
    }
}
