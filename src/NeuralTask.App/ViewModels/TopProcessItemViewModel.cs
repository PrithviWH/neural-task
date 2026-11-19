using System;

namespace NeuralTask.App.ViewModels;

public class TopProcessItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private int _pid;
    private string _subtext = string.Empty;
    private string _valueDisplay = string.Empty;
    private double _percentValue;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public int Pid
    {
        get => _pid;
        set => SetProperty(ref _pid, value);
    }

    public string Subtext
    {
        get => _subtext;
        set => SetProperty(ref _subtext, value);
    }

    public string ValueDisplay
    {
        get => _valueDisplay;
        set => SetProperty(ref _valueDisplay, value);
    }

    public double PercentValue
    {
        get => _percentValue;
        set => SetProperty(ref _percentValue, value);
    }
}
