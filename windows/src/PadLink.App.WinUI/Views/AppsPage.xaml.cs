using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PadLink.App.ViewModels;
using PadLink.Core;

namespace PadLink.App.Views;

public sealed partial class AppsPage : Page
{
    public ShellViewModel Vm { get; private set; } = null!;

    public AppsPage()
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
            vm.Apps.CollectionChanged += (_, _) => UpdateCount();
            UpdateCount();
        }
    }

    private void UpdateCount()
    {
        if (Vm == null) return;
        CountLabel.Text = $"共 {Vm.Apps.Count} 项";
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        Vm.Apps.Add(new AppEntryModel { Name = "", Cmd = "" });
        UpdateCount();
        // 滚到新行
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Vm.Apps.Count > 0)
                AppsList.ScrollIntoView(Vm.Apps[^1]);
        });
    }

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppEntryModel entry)
        {
            Vm.Apps.Remove(entry);
            UpdateCount();
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var before = Vm.Apps.Count;
        var valid = Vm.Apps
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Cmd))
            .Select(a => new AppEntryModel { Name = a.Name.Trim(), Cmd = a.Cmd.Trim() })
            .ToList();
        var skipped = before - valid.Count;

        AppsStore.Save(valid);
        Vm.Apps.Clear();
        foreach (var a in valid) Vm.Apps.Add(a);
        UpdateCount();

        var msg = $"已保存 {valid.Count} 个应用。";
        if (skipped > 0) msg += $"\n（跳过 {skipped} 个名称为空或命令为空的行）";
        msg += "\n\n手机端打开「应用」页点刷新即可看到。";

        var dialog = new ContentDialog
        {
            Title = "PadLink",
            Content = msg,
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "重新载入",
            Content = "会丢弃当前未保存的修改，确定继续？",
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Vm.LoadApps();
            UpdateCount();
        }
    }
}
