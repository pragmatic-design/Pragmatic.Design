using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Options;

namespace Pragmatic.Discovery.Tests.Unit;

public class DiscoveryOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new DiscoveryOptions();

        options.AutoRegisterOnStartup.Should().BeTrue();
        options.ValidateOnStartup.Should().BeTrue();
        options.ThrowOnValidationFailure.Should().BeFalse();
    }

    [Fact]
    public void SectionName_IsPragmaticDiscovery()
    {
        DiscoveryOptions.SectionName.Should().Be("Pragmatic:Discovery");
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var options = new DiscoveryOptions
        {
            AutoRegisterOnStartup = false,
            ValidateOnStartup = false,
            ThrowOnValidationFailure = true
        };

        options.AutoRegisterOnStartup.Should().BeFalse();
        options.ValidateOnStartup.Should().BeFalse();
        options.ThrowOnValidationFailure.Should().BeTrue();
    }
}
