using System.Windows;
using System.Windows.Controls;
using NeuralTask.App.ViewModels;

namespace NeuralTask.App.Views;

public partial class ProcessListView : UserControl
{
    private ProcessListViewModel? ViewModel => DataContext as ProcessListViewModel;

    public ProcessListView()
    {
        InitializeComponent();
    }

    private void OnHeaderNameClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("Name");
    private void OnHeaderPidClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("Pid");
    private void OnHeaderCpuClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("CpuPercent");
    private void OnHeaderRamClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("RamBytes");
    private void OnHeaderIoClick(object sender, RoutedEventArgs e) => ViewModel?.SortBy("DiskBytesPerSec");
}
