using System.Reflection;

namespace Pragmatic.Notes.Tests;

public class HasNotesAttributeTests
{
    private sealed class Parent
    {
    }

    [Fact]
    public void Defaults_MaxLength_Is4000()
    {
        new HasNotesAttribute().MaxLength.Should().Be(4000);
    }

    [Fact]
    public void Defaults_AllowEditing_IsTrue()
    {
        new HasNotesAttribute().AllowEditing.Should().BeTrue();
    }

    [Fact]
    public void Defaults_EditWindowMinutes_IsMinusOne()
    {
        new HasNotesAttribute().EditWindowMinutes.Should().Be(-1);
    }

    [Fact]
    public void Defaults_SubBoundary_IsNull()
    {
        new HasNotesAttribute().SubBoundary.Should().BeNull();
    }

    [Fact]
    public void Properties_AllOptions_AreSettable()
    {
        var attribute = new HasNotesAttribute
        {
            MaxLength = 280,
            AllowEditing = false,
            EditWindowMinutes = 15,
            SubBoundary = "ReservationNotes",
        };

        attribute.MaxLength.Should().Be(280);
        attribute.AllowEditing.Should().BeFalse();
        attribute.EditWindowMinutes.Should().Be(15);
        attribute.SubBoundary.Should().Be("ReservationNotes");
    }

    [Fact]
    public void EditWindowMinutes_MinusOne_RepresentsNoLimit()
    {
        new HasNotesAttribute { EditWindowMinutes = -1 }.EditWindowMinutes.Should().Be(-1);
    }

    [Fact]
    public void EditWindowMinutes_Zero_IsSettable()
    {
        new HasNotesAttribute { EditWindowMinutes = 0 }.EditWindowMinutes.Should().Be(0);
    }

    [Fact]
    public void Type_IsSealed()
    {
        typeof(HasNotesAttribute).IsSealed.Should().BeTrue();
    }

    [Fact]
    public void Type_DerivesFromAttribute()
    {
        typeof(HasNotesAttribute).Should().BeDerivedFrom<Attribute>();
    }



    [Fact]
    public void AttributeUsage_TargetsClassOnly()
    {
        var usage = typeof(HasNotesAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.ValidOn.Should().Be(AttributeTargets.Class);
    }

    [Fact]
    public void AttributeUsage_IsNotInherited()
    {
        var usage = typeof(HasNotesAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        usage.Inherited.Should().BeFalse();
    }

    [HasNotes(MaxLength = 100, AllowEditing = false, EditWindowMinutes = 30, SubBoundary = "Sub")]
    private sealed class DecoratedEntity
    {
    }

    [Fact]
    public void Applied_AsAttribute_RoundTripsNamedArguments()
    {
        var attribute = typeof(DecoratedEntity).GetCustomAttribute<HasNotesAttribute>();

        attribute.Should().NotBeNull();
        attribute!.MaxLength.Should().Be(100);
        attribute.AllowEditing.Should().BeFalse();
        attribute.EditWindowMinutes.Should().Be(30);
        attribute.SubBoundary.Should().Be("Sub");
    }
}
