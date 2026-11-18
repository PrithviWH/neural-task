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
            ViewModel.RequestScannerSweep += PlayScannerSweepAnimation;
        }
    }

    private void OnReScanClick(object sender, RoutedEventArgs e)
    {
        ViewModel?.TriggerReScan();
    }

    private void PlayScannerSweepAnimation()
    {
        var height = Math.Max(ActualHeight - 120, 400);

        var moveAnim = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 0,
            To = height,
            Duration = System.TimeSpan.FromMilliseconds(750),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
        };

        var opacityAnim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(System.TimeSpan.Zero)));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(100))));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(650))));
        opacityAnim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(System.TimeSpan.FromMilliseconds(750))));

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
