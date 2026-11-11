using Microsoft.UI.Xaml;
using PadLink.App.Services;
using PadLink.Core;

namespace PadLink.App;

public partial class App : Application
{
    private MainWindow? _window;
    private TrayIcon? _tray;
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    public PadLinkSettings Settings { get; private set; } = new();

    public static App Current => (App)Application.Current;

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            Log.Error("UI 未处理异常", e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // ---- 单实例 ----
        _mutex = new Mutex(true, PadLinkConstants.MutexName, out bool isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            PadLinkConstants.ShowEventName);

        if (!isFirst)
        {
            // 已有实例：通知它显示窗口，然后退出
            _showEvent.Set();
            Log.Info("检测到已有实例，唤回窗口后退出");
            Exit();
            return;
        }

        Settings = SettingsStore.Load();
        Log.Info($"PadLink 启动 v2.0.0 端口={Settings.Port}");

        // ---- 启动 WebSocket/HTTP 服务（后台线程）----
        ServerHost.Start(Settings.Port);

        // ---- 主窗口 ----
        _window = new MainWindow(Settings);

        // 启动策略：
        //  - 带 --tray 参数（开机自启注册表写入的）→ 一律隐藏到托盘
        //  - 否则按用户设置 StartMinimized 决定是否显示窗口
        bool launchedForTray = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        bool showWindow = !launchedForTray && !Settings.StartMinimized;

        if (showWindow)
        {
            _window.Activate();
            Log.Info("启动：显示主窗口");
        }
        else
        {
            // 不激活窗口，仅常驻托盘 + 后台服务
            _window.HideToTray();
            Log.Info(launchedForTray ? "启动：开机自启，隐藏到托盘" : "启动：最小化到托盘");
        }

        // ---- 托盘 ----
        _tray = new TrayIcon();
        _tray.ShowRequested += () => _window?.ShowFromTray();
        _tray.ExitRequested += () => ExitApp();
        _tray.Create();

        // ---- 监听"唤回窗口"事件 ----
        var waiter = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!_showEvent.WaitOne()) break;
                    _window?.DispatcherQueue.TryEnqueue(() => _window?.ShowFromTray());
                }
                catch { break; }
            }
        }) { IsBackground = true, Name = "ShowWaiter" };
        waiter.Start();
    }

    public void ExitApp()
    {
        Log.Info("正在退出 PadLink");
        try { ServerHost.Stop(); } catch { }
        _tray?.Dispose();
        _tray = null;
        _window?.CloseForExit();
        Exit();
    }

    public async void OpenControlPage()
    {
        var url = $"http://{NetworkInfo.LocalIP}:{Settings.Port}";
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
        catch (Exception ex) { Log.Error("打开浏览器失败", ex); }
    }
}
