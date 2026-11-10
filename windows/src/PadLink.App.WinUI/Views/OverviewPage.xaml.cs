using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PadLink.App.ViewModels;

namespace PadLink.App.Views;

public sealed partial class OverviewPage : Page
{
    public ShellViewModel Vm { get; private set; } = null!;

    public string IpLine => Vm == null
        ? ""
        : $"局域网 IP {Vm.LocalIp} · 端口 {Vm.SettingsRef.Port}";

    public OverviewPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ShellViewModel vm)
        {
            Vm = vm;
            DataContext = vm;
        }
    }
}
