using System.Windows;
using System.Windows.Input;
using NeuralTask.Core.Models;

namespace NeuralTask.App.Views;

public partial class ConfirmKillDialog : Window
{
    public bool IsConfirmed { get; private set; }

    public ConfirmKillDialog(ProcessSnapshot target)
    {
        InitializeComponent();

        TextProcessName.Text = string.IsNullOrEmpty(target.Name) ? $"PID {target.Pid}" : target.Name;
        TextPid.Text = $"PID: {target.Pid}";
        TextExecutablePath.Text = string.IsNullOrEmpty(target.ExecutablePath) ? "Executable path unavailable" : target.ExecutablePath;

        DialogHeaderBar.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        };
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        IsConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
        Close();
    }
}
