using System.Windows.Controls;

namespace NeuralTask.Plugins.Gpu.Views;

public partial class GpuPanelView : UserControl
{
    public GpuPanelView(GpuPanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
