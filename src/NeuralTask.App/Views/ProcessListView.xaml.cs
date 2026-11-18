using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NeuralTask.App.ViewModels;
using NeuralTask.Core.Models;

namespace NeuralTask.App.Views;

public partial class ProcessListView : UserControl
{
    private ProcessListViewModel? ViewModel => DataContext as ProcessListViewModel;

    public ProcessListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ConfirmKillTreeCallback = ShowKillTreeDialog;
        }
    }

    private Task<(bool confirmed, bool entireTree)> ShowKillTreeDialog(ProcessSnapshot root, IReadOnlyList<ProcessSnapshot> children)
    {
        var ownerWindow = Window.GetWindow(this);
        var dialog = new KillTreeDialog(root, children);
        if (ownerWindow != null)
        {
            dialog.Owner = ownerWindow;
        }

        dialog.ShowDialog();
        return Task.FromResult((dialog.IsConfirmed, dialog.TerminateEntireTree));
    }

    private async void OnKillClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateSelectedAsync();
        }
    }

    private async void OnTerminateTreeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateSelectedAsync();
        }
    }

    private async void OnContextMenuKillClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateSelectedAsync();
        }
    }

    private async void OnContextMenuKillTreeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateSelectedAsync();
        }
    }

    private void OnDismissBannerClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissStatus();
    }

    private void OnHeaderNameClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("Name");
    private void OnHeaderPidClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("Pid");
    private void OnHeaderCpuClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("CpuPercent");
    private void OnHeaderRamClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("RamBytes");
    private void OnHeaderIoClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("DiskBytesPerSec");
}
