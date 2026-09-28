using Macrofy.Platform.Models;
using Xunit;
namespace Macrofy.Core.Tests;
public class ContractsTests
{
    [Fact]
    public void NeutralModelsExposeNoNativeHandles()
    {
        Assert.Equal(typeof(Guid), typeof(TargetToken).GetProperty("Id")!.PropertyType);
        Assert.DoesNotContain(typeof(TargetWindow).GetProperties(), p => p.PropertyType == typeof(IntPtr));
    }
    [Fact]
    public void QueuedDeliveryIsNotObservedGameplaySuccess()
    {
        var delivery = new DeliveryResult(true);
        Assert.True(delivery.Queued);
        Assert.Null(delivery.Error);
        Assert.Null(typeof(DeliveryResult).GetProperty("ObservedSuccess"));
    }
}

