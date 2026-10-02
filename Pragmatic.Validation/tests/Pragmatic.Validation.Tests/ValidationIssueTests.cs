using Pragmatic.Testing.Assertions;
using Pragmatic.Validation.Types;
using Xunit;

namespace Pragmatic.Validation.Tests;

public class ValidationIssueTests
{
    // =========================================================================
    // Constructor Tests
    // =========================================================================

    [Fact]
    public void Constructor_WithMessageKeyOnly_CreatesIssue()
    {
        var issue = new ValidationIssue("validation.required");

        issue.MessageKey.Should().Be("validation.required");
        issue.PropertyPath.Should().BeNull();
        issue.Parameters.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageKeyAndPath_CreatesIssue()
    {
        var issue = new ValidationIssue("validation.required", "Email");

        issue.MessageKey.Should().Be("validation.required");
        issue.PropertyPath.Should().Be("Email");
        issue.Parameters.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithParameters_CreatesIssueWithParams()
    {
        var issue = new ValidationIssue("validation.minlength", "Name", ("min", 3));

        issue.MessageKey.Should().Be("validation.minlength");
        issue.PropertyPath.Should().Be("Name");
        issue.Parameters.Should().NotBeNull();
        issue.Parameters.Should().ContainKey("min");
        issue.Parameters!["min"].Should().Be(3);
    }

    [Fact]
    public void Constructor_WithMultipleParameters_CreatesIssueWithAllParams()
    {
        var issue = new ValidationIssue("validation.range", "Age", ("min", 18), ("max", 65));

        issue.Parameters.Should().HaveCount(2);
        issue.Parameters!["min"].Should().Be(18);
        issue.Parameters!["max"].Should().Be(65);
    }

    [Fact]
    public void Constructor_WithNullMessageKey_Throws()
    {
        var act = () => new ValidationIssue(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithEmptyMessageKey_Throws()
    {
        var act = () => new ValidationIssue("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithDictionary_CreatesIssueWithParams()
    {
        var parameters = new Dictionary<string, object> { ["min"] = 5, ["max"] = 10 };
        var issue = new ValidationIssue("validation.range", "Value", parameters);

        issue.Parameters.Should().NotBeNull();
        issue.Parameters.Should().HaveCount(2);
    }

    // =========================================================================
    // Factory Methods
    // =========================================================================

    [Fact]
    public void For_CreatesIssueWithPathFirst()
    {
        var issue = ValidationIssue.For("Email", "validation.required");

        issue.PropertyPath.Should().Be("Email");
        issue.MessageKey.Should().Be("validation.required");
    }

    [Fact]
    public void For_WithParameters_CreatesIssueWithParams()
    {
        var issue = ValidationIssue.For("Name", "validation.minlength", ("min", 3));

        issue.PropertyPath.Should().Be("Name");
        issue.MessageKey.Should().Be("validation.minlength");
        issue.Parameters!["min"].Should().Be(3);
    }

    [Fact]
    public void ForNested_CreatesIndexedPath()
    {
        var issue = ValidationIssue.ForNested("Items", 0, "ProductId", "validation.required");

        issue.PropertyPath.Should().Be("Items[0].ProductId");
        issue.MessageKey.Should().Be("validation.required");
    }

    [Fact]
    public void ForNested_WithParameters_CreatesIndexedPathWithParams()
    {
        var issue = ValidationIssue.ForNested("Items", 2, "Quantity", "validation.range", ("min", 1), ("max", 100));

        issue.PropertyPath.Should().Be("Items[2].Quantity");
        issue.Parameters!["min"].Should().Be(1);
        issue.Parameters!["max"].Should().Be(100);
    }

    [Fact]
    public void ForObject_CreatesIssueWithoutPath()
    {
        var issue = ValidationIssue.ForObject("validation.cross_field");

        issue.PropertyPath.Should().BeNull();
        issue.MessageKey.Should().Be("validation.cross_field");
    }

    [Fact]
    public void ForObject_WithParameters_CreatesIssueWithParams()
    {
        var issue = ValidationIssue.ForObject("validation.date_range", ("start", "StartDate"), ("end", "EndDate"));

        issue.PropertyPath.Should().BeNull();
        issue.Parameters.Should().HaveCount(2);
    }

    // =========================================================================
    // ToString Tests
    // =========================================================================

    [Fact]
    public void ToString_WithPropertyPathOnly_ReturnsFormattedString()
    {
        var issue = new ValidationIssue("validation.required", "Email");

        issue.ToString().Should().Be("Email: validation.required");
    }

    [Fact]
    public void ToString_WithoutPropertyPath_ReturnsMessageKey()
    {
        var issue = ValidationIssue.ForObject("validation.cross_field");

        issue.ToString().Should().Be("validation.cross_field");
    }

    [Fact]
    public void ToString_WithParameters_IncludesParamCount()
    {
        var issue = new ValidationIssue("validation.minlength", "Name", ("min", 3));

        issue.ToString().Should().Contain("params: 1");
    }

    [Fact]
    public void ToString_WithMultipleParameters_ShowsCorrectCount()
    {
        var issue = new ValidationIssue("validation.range", "Age", ("min", 18), ("max", 65));

        issue.ToString().Should().Contain("params: 2");
    }

    // =========================================================================
    // Equality Tests (record struct)
    // =========================================================================

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var issue1 = new ValidationIssue("validation.required", "Email");
        var issue2 = new ValidationIssue("validation.required", "Email");

        issue1.Should().Be(issue2);
        (issue1 == issue2).Should().BeTrue();
    }

    [Fact]
    public void Equals_DifferentMessageKey_ReturnsFalse()
    {
        var issue1 = new ValidationIssue("validation.required", "Email");
        var issue2 = new ValidationIssue("validation.format", "Email");

        issue1.Should().NotBe(issue2);
    }

    [Fact]
    public void Equals_DifferentPropertyPath_ReturnsFalse()
    {
        var issue1 = new ValidationIssue("validation.required", "Email");
        var issue2 = new ValidationIssue("validation.required", "Name");

        issue1.Should().NotBe(issue2);
    }

    [Fact]
    public void GetHashCode_SameValues_ReturnsSameHash()
    {
        var issue1 = new ValidationIssue("validation.required", "Email");
        var issue2 = new ValidationIssue("validation.required", "Email");

        issue1.GetHashCode().Should().Be(issue2.GetHashCode());
    }
}