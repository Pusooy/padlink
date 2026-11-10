using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PadLink.App.ViewModels;
using PadLink.Core;

namespace PadLink.App.Views;

public sealed partial class ClientsPage : Page
{
    public ShellViewModel Vm { get; private set; } = null!;
    private bool _loading;

    public ClientsPage()
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

            _loading = true;
            AutoStartToggle.IsOn = vm.SettingsRef.AutoStart;
            StartMinToggle.IsOn = vm.SettingsRef.StartMinimized;
            AutoOpenToggle.IsOn = vm.SettingsRef.AutoOpenBrowser;
            _loading = false;
        }
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || Vm == null) return;
        Vm.SettingsRef.AutoStart = AutoStartToggle.IsOn;
        Services.StartupRegistration.Apply(AutoStartToggle.IsOn);
        SettingsStore.Save(Vm.SettingsRef);
    }

    private void StartMin_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || Vm == null) return;
        Vm.SettingsRef.StartMinimized = StartMinToggle.IsOn;
        SettingsStore.Save(Vm.SettingsRef);
    }

    private void AutoOpen_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || Vm == null) return;
        Vm.SettingsRef.AutoOpenBrowser = AutoOpenToggle.IsOn;
        SettingsStore.Save(Vm.SettingsRef);
    }
}
