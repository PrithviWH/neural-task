using System;
using NeuralTask.Core.Services;

namespace NeuralTask.App.ViewModels;

/// <summary>
/// Typed factory for creating ProcessInspectorViewModel instances with runtime parameter binding.
/// </summary>
public interface IProcessInspectorViewModelFactory
{
    ProcessInspectorViewModel Create(ProcessItemViewModel item);
}

/// <summary>
/// Default implementation of IProcessInspectorViewModelFactory injecting IProcessInfoService.
/// </summary>
public class ProcessInspectorViewModelFactory : IProcessInspectorViewModelFactory
{
    private readonly IProcessInfoService _processInfoService;

    public ProcessInspectorViewModelFactory(IProcessInfoService processInfoService)
    {
        _processInfoService = processInfoService ?? throw new ArgumentNullException(nameof(processInfoService));
    }

    public ProcessInspectorViewModel Create(ProcessItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ProcessInspectorViewModel(item, _processInfoService);
    }
}
