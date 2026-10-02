using Pragmatic.Testing.Assertions;
using Pragmatic.ControlPlane;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class HostCommandTests
{
    // ExitMaintenanceCommand is the simplest concrete HostCommand (parameterless ctor).
    [Fact]
    public void CommandId_DefaultsToNonEmptyString()
    {
        var command = new ExitMaintenanceCommand();

        command.CommandId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CommandId_IsUniquePerInstance()
    {
        var first = new ExitMaintenanceCommand();
        var second = new ExitMaintenanceCommand();

        first.CommandId.Should().NotBe(second.CommandId);
    }

    [Fact]
    public void CommandId_PreservedWhenSetViaInit()
    {
        var command = new ExitMaintenanceCommand { CommandId = "fixed-id" };

        command.CommandId.Should().Be("fixed-id");
    }

    [Fact]
    public void CorrelationId_DefaultsToNull()
    {
        var command = new ExitMaintenanceCommand();

        command.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void CorrelationId_PreservedWhenSetViaInit()
    {
        var command = new ExitMaintenanceCommand { CorrelationId = "corr-42" };

        command.CorrelationId.Should().Be("corr-42");
    }

    [Fact]
    public void CommandTypeName_MatchesConcreteType()
    {
        HostCommand command = new ExitMaintenanceCommand();

        command.CommandTypeName.Should().Be(nameof(ExitMaintenanceCommand));
    }
}
