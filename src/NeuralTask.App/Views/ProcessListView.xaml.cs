using System;
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

    private readonly Dictionary<string, double> _defaultColumnWidths = new()
    {
        { "ColPid", 65.0 },
        { "ColCpu", 110.0 },
        { "ColRam", 120.0 },
        { "ColIo", 85.0 },
        { "ColNet", 75.0 },
        { "ColParentPid", 65.0 },
        { "ColStartTime", 95.0 }
    };

    public ProcessListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsInspectorVisible = true;
            if (ViewModel.SelectedProcess == null && ViewModel.FilteredProcesses.Count > 0)
            {
                ViewModel.SelectedProcess = ViewModel.FilteredProcesses[0];
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ProcessListViewModel oldVm)
        {
            oldVm.FilteredProcesses.CollectionChanged -= OnFilteredProcessesChanged;
        }

        if (ViewModel != null)
        {
            ViewModel.ConfirmKillTreeCallback = ShowKillTreeDialog;
            ViewModel.ConfirmKillProcessCallback = ShowConfirmKillDialog;
            ViewModel.RequestScannerSweep += PlayScannerSweepAnimation;

            ViewModel.FilteredProcesses.CollectionChanged += OnFilteredProcessesChanged;
            if (ViewModel.SelectedProcess == null && ViewModel.FilteredProcesses.Count > 0)
            {
                ViewModel.SelectedProcess = ViewModel.FilteredProcesses[0];
            }
        }
    }

    private void OnFilteredProcessesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel != null && ViewModel.SelectedProcess == null && ViewModel.FilteredProcesses.Count > 0 && ViewModel.IsInspectorVisible)
        {
            ViewModel.SelectedProcess = ViewModel.FilteredProcesses[0];
        }
    }

    private void OnReScanClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TriggerReScan();
    }

    private void PlayScannerSweepAnimation()
    {
        var height = Math.Max(ActualHeight - 80, 300);

        var moveAnim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = height,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };

        var opacityAnim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.25, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80))));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.25, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320))));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));

        System.Windows.Media.Animation.Storyboard.SetTarget(moveAnim, ScannerTransform);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(moveAnim, new PropertyPath(System.Windows.Media.TranslateTransform.YProperty));

        System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnim, ScannerBeam);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));

        var sb = new System.Windows.Media.Animation.Storyboard();
        sb.Children.Add(moveAnim);
        sb.Children.Add(opacityAnim);
        sb.Begin();
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

    private Task<bool> ShowConfirmKillDialog(ProcessSnapshot target)
    {
        var ownerWindow = Window.GetWindow(this);
        var dialog = new ConfirmKillDialog(target);
        if (ownerWindow != null)
        {
            dialog.Owner = ownerWindow;
        }

        dialog.ShowDialog();
        return Task.FromResult(dialog.IsConfirmed);
    }

    private void OnGridViewColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader header && header.Column != null)
        {
            if (header.Column.Header is FrameworkElement fe && fe.Tag is string tag)
            {
                ViewModel?.SortBy(tag);
            }
            else if (header.Tag is string headerTag)
            {
                ViewModel?.SortBy(headerTag);
            }
        }
    }

    private void OnCloseInspectorClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsInspectorVisible = false;
        }
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.SearchText = string.Empty;
        }
    }

    private void OnContextMenuInspectClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsInspectorVisible = true;
        }
    }

    private void OnContextMenuCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel?.SelectedProcess?.ExecutablePath != null)
        {
            System.Windows.Clipboard.SetText(ViewModel.SelectedProcess.ExecutablePath);
            ViewModel.ShowStatus($"Copied executable path for {ViewModel.SelectedProcess.Name}", false);
        }
    }

    private void OnContextMenuOpenFolderClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.OpenContainingFolder();
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
            await ViewModel.TerminateTreeSelectedAsync();
        }
    }

    private async void OnContextMenuKillClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateSelectedAsync();
        }
    }

    private async void OnCopyInfoClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.CopyProcessInfoAsync();
        }
    }

    private async void OnContextMenuCopyInfoClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.CopyProcessInfoAsync();
        }
    }

    private async void OnContextMenuKillTreeClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.TerminateTreeSelectedAsync();
        }
    }

    private void OnDismissBannerClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.DismissStatus();
    }
}
