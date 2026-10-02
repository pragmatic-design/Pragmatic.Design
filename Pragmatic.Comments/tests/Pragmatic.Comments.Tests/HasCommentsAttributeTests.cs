using Pragmatic.Testing.Assertions;
using Pragmatic.Comments;

namespace Pragmatic.Comments.Tests;

public class HasCommentsAttributeTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var attr = new HasCommentsAttribute();

        attr.MaxLength.Should().Be(2000);
        attr.AllowReplies.Should().BeTrue();
        attr.AllowEditing.Should().BeTrue();
        attr.EditWindowMinutes.Should().Be(-1);
        attr.RequireApproval.Should().BeFalse();
        attr.SupportInternalNotes.Should().BeFalse();
        attr.SubBoundary.Should().BeNull();
    }

    [Fact]
    public void CustomValues_AreApplied()
    {
        var attr = new HasCommentsAttribute
        {
            MaxLength = 500,
            AllowReplies = false,
            AllowEditing = false,
            EditWindowMinutes = 30,
            RequireApproval = true,
            SupportInternalNotes = true,
            SubBoundary = "CustomComments"
        };

        attr.MaxLength.Should().Be(500);
        attr.AllowReplies.Should().BeFalse();
        attr.AllowEditing.Should().BeFalse();
        attr.EditWindowMinutes.Should().Be(30);
        attr.RequireApproval.Should().BeTrue();
        attr.SupportInternalNotes.Should().BeTrue();
        attr.SubBoundary.Should().Be("CustomComments");
    }

    /// <summary>
    ///     EditWindowMinutes is an <c>int</c> with <c>-1</c> for "no limit", because <c>int?</c> is not a
    ///     legal attribute-argument type: this usage would not compile (CS0655) and the generator code
    ///     reading it would be dead.
    /// </summary>
    [Fact]
    public void EditWindowMinutes_IsUsableAsAttributeArgument()
    {
        var attr = typeof(EditWindowSample)
            .GetCustomAttributes(typeof(HasCommentsAttribute), false)
            .Cast<HasCommentsAttribute>()
            .Single();

        attr.EditWindowMinutes.Should().Be(30);
    }

    [Fact]
    public void EditWindowMinutes_MinusOne_MeansNoLimit()
        => new HasCommentsAttribute { EditWindowMinutes = -1 }.EditWindowMinutes.Should().Be(-1);

    [HasComments(EditWindowMinutes = 30)]
    private sealed class EditWindowSample;
}
