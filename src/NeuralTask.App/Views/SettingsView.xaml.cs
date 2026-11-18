using System.Windows.Controls;

namespace NeuralTask.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    public void ScrollToBottom() => SettingsScrollViewer.ScrollToEnd();
    public void ScrollToTop() => SettingsScrollViewer.ScrollToTop();
}
