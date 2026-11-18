using NeuralTask.Core;
using Xunit;

namespace NeuralTask.Core.Tests;

public class SystemMonitorPlaceholderTests
{
    [Fact]
    public void SystemMonitorPlaceholder_InitializesSuccessfully()
    {
        // Arrange
        var service = new SystemMonitorPlaceholder();

        // Assert
        Assert.NotNull(service);
        Assert.True(service.IsInitialized);
        Assert.Equal("NeuralTask.Core.MonitorService", service.ServiceName);
    }
}
