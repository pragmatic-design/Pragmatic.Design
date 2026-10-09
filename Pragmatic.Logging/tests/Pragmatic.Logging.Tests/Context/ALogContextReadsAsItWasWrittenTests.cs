using Pragmatic.Logging.Context;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests.Context;

/// <summary>
///     A context reads back what was written to it, as a dictionary would: a key set again keeps its place and
///     takes the new value, a removed key is gone, a child's value wins over its parent's.
/// </summary>
/// <remarks>
///     The properties are an immutable snapshot published on each write, not a dictionary; these pin the
///     behaviour the dictionary gave, including a snapshot taken before a write not seeing it.
/// </remarks>
public class ALogContextReadsAsItWasWrittenTests
{
    [Fact]
    public void SetAgain_KeepsThePlaceAndTakesTheValue()
    {
        var context = new LogContext();
        context.SetProperty("A", 1);
        context.SetProperty("B", 2);
        context.SetProperty("A", 3);

        context.Properties.Select(property => property.Key).Should().Equal("A", "B");
        context.GetProperty("A").Should().Be(3);
        context.Properties["B"].Should().Be(2);
        context.Properties.Count.Should().Be(2);
    }

    [Fact]
    public void Remove_TheKeyIsGoneAndTheOthersStay()
    {
        var context = new LogContext();
        context.SetProperty("A", 1);
        context.SetProperty("B", 2);
        context.SetProperty("C", 3);

        context.RemoveProperty("B").Should().BeTrue();
        context.RemoveProperty("B").Should().BeFalse();

        context.Properties.Select(property => property.Key).Should().Equal("A", "C");
        context.HasProperty("B").Should().BeFalse();
        context.Properties.TryGetValue("C", out var c).Should().BeTrue();
        c.Should().Be(3);
    }

    [Fact]
    public void Clear_LeavesNothing()
    {
        var context = new LogContext();
        context.SetProperty("A", 1);

        context.Clear();

        context.Properties.Count.Should().Be(0);
        context.GetProperty("A").Should().BeNull();
    }

    [Fact]
    public void ASnapshotTakenBeforeAWrite_DoesNotSeeIt()
    {
        var context = new LogContext();
        context.SetProperty("A", 1);
        var before = context.Properties;

        context.SetProperty("A", 2);
        context.SetProperty("B", 3);

        before["A"].Should().Be(1);
        before.ContainsKey("B").Should().BeFalse();
    }

    [Fact]
    public void AChild_ReadsItsParentAndWinsOverIt()
    {
        var parent = new LogContext();
        parent.SetProperty("Tenant", "acme");
        parent.SetProperty("User", "parent");
        var child = parent.CreateChild();
        child.SetProperty("User", "child");

        child.GetProperty("Tenant").Should().Be("acme");
        child.GetProperty("User").Should().Be("child");
        child.HasProperty("Tenant").Should().BeTrue();
        child.Properties["User"].Should().Be("child");
        child.Properties.Count.Should().Be(2);
    }

    [Fact]
    public void WriteProperties_WritesWhatPropertiesHolds_ThroughTheFilter()
    {
        var parent = new LogContext();
        parent.SetProperty("Tenant", "acme");
        parent.SetProperty("User", "parent");
        parent.SetProperty("Secret", "s");
        var child = parent.CreateChild();
        child.SetProperty("User", "child");

        var written = new Dictionary<string, object?>();
        child.WriteProperties(written, key => key != "Secret");

        written.Should().HaveCount(2);
        written["Tenant"].Should().Be("acme");
        written["User"].Should().Be("child");
    }
}
