using Pragmatic.Testing.Assertions;
using Showcase.Billing.Entities;
using Xunit;

namespace Showcase.Tests.Unit.Entities;

/// <summary>
/// Tests that the [Inheritance(Tph)] on Fee generates the InheritanceConfiguration.
/// </summary>
public class FeeInheritanceTests
{
    [Fact]
    public void FeeInheritanceConfiguration_ClassExists()
    {
        // The SG generates FeeInheritanceConfiguration in the same namespace
        var type = typeof(Fee).Assembly.GetType("Showcase.Billing.Entities.FeeInheritanceConfiguration");
        type.Should().NotBeNull("SG should generate FeeInheritanceConfiguration");
    }

    [Fact]
    public void FeeInheritanceConfiguration_HasConfigureMethod()
    {
        var type = typeof(Fee).Assembly.GetType("Showcase.Billing.Entities.FeeInheritanceConfiguration");
        type.Should().NotBeNull();

        var method = type!.GetMethod("Configure",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        method.Should().NotBeNull("Configure method should exist");
        method!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void ServiceFee_DerivesFee()
    {
        typeof(ServiceFee).Should().BeDerivedFrom<Fee>();
    }

    [Fact]
    public void CancellationFee_DerivesFee()
    {
        typeof(CancellationFee).Should().BeDerivedFrom<Fee>();
    }
}
