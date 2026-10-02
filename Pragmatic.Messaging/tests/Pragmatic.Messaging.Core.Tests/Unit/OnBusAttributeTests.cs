using Pragmatic.Testing.Assertions;
using Pragmatic.Messaging.Attributes;

namespace Pragmatic.Messaging.Core.Tests.Unit;

public class OnBusAttributeTests
{
    [Fact]
    public void Constructor_SetsBusName()
    {
        // Regression: without [SetsRequiredMembers] on the constructor this would not compile (CS9035).
        var attribute = new OnBusAttribute("integration");

        attribute.BusName.Should().Be("integration");
    }

    [OnBus("analytics")]
    private sealed class AnnotatedHandler;

    [Fact]
    public void Attribute_AppliedViaConstructor_ExposesBusName()
    {
        var attribute = (OnBusAttribute)typeof(AnnotatedHandler)
            .GetCustomAttributes(typeof(OnBusAttribute), false)[0];

        attribute.BusName.Should().Be("analytics");
    }
}
