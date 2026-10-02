using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

/// <summary>
///     Tests for non-indexed WithNested and ForNested overloads (nested object validation).
/// </summary>
public class ValidationErrorNestedTests
{
    [Fact]
    public void WithNested_ParentAndChild_FormsDottedPath()
    {
        var error = ValidationError.Valid
            .WithNested("Address", "Street", "validation.required");

        error.IsFailure.Should().BeTrue();
        error.Issues[0].PropertyPath.Should().Be("Address.Street");
        error.Issues[0].MessageKey.Should().Be("validation.required");
    }

    [Fact]
    public void WithNested_EmptyChildPath_ReturnsParentOnly()
    {
        var error = ValidationError.Valid
            .WithNested("Address", "", "validation.required");

        error.Issues[0].PropertyPath.Should().Be("Address");
    }

    [Fact]
    public void ForNested_NonIndexed_FormsDottedPath()
    {
        var issue = ValidationIssue.ForNested("Address", "ZipCode", "validation.format");

        issue.PropertyPath.Should().Be("Address.ZipCode");
        issue.MessageKey.Should().Be("validation.format");
    }

    [Fact]
    public void CombinedIndexedAndNonIndexed_FormsCorrectPath()
    {
        var error = ValidationError.Valid
            .WithNested("Items", 0, "Address", "validation.required")
            .WithNested("Items[0].Address", "Street", "validation.min_length");

        error.Count.Should().Be(2);
        error.Issues[0].PropertyPath.Should().Be("Items[0].Address");
        error.Issues[1].PropertyPath.Should().Be("Items[0].Address.Street");
    }
}
