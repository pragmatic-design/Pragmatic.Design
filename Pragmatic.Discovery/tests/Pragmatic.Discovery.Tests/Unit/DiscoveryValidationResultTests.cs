using Pragmatic.Testing.Assertions;
using Pragmatic.Discovery.Models;

namespace Pragmatic.Discovery.Tests.Unit;

public class DiscoveryValidationResultTests
{
    [Fact]
    public void Ok_IsValid()
    {
        DiscoveryValidationResult.Ok().IsValid.Should().BeTrue();
    }

    [Fact]
    public void Ok_HasNoIssues()
    {
        DiscoveryValidationResult.Ok().Issues.Should().BeEmpty();
    }

    [Fact]
    public void Errors_FiltersOnlyErrorSeverity()
    {
        var result = new DiscoveryValidationResult
        {
            IsValid = false,
            Issues =
            [
                new DiscoveryValidationIssue { Code = "E1", Message = "Error", Severity = IssueSeverity.Error },
                new DiscoveryValidationIssue { Code = "W1", Message = "Warning", Severity = IssueSeverity.Warning },
                new DiscoveryValidationIssue { Code = "I1", Message = "Info", Severity = IssueSeverity.Info }
            ]
        };

        result.Errors.Should().ContainSingle().Which.Code.Should().Be("E1");
    }

    [Fact]
    public void Warnings_FiltersOnlyWarningSeverity()
    {
        var result = new DiscoveryValidationResult
        {
            IsValid = true,
            Issues =
            [
                new DiscoveryValidationIssue { Code = "E1", Message = "Error", Severity = IssueSeverity.Error },
                new DiscoveryValidationIssue { Code = "W1", Message = "Warning", Severity = IssueSeverity.Warning },
                new DiscoveryValidationIssue { Code = "I1", Message = "Info", Severity = IssueSeverity.Info }
            ]
        };

        result.Warnings.Should().ContainSingle().Which.Code.Should().Be("W1");
    }

    [Fact]
    public void Errors_WithNoErrorIssues_ReturnsEmpty()
    {
        var result = new DiscoveryValidationResult
        {
            IsValid = true,
            Issues =
            [
                new DiscoveryValidationIssue { Code = "I1", Message = "Info", Severity = IssueSeverity.Info }
            ]
        };

        result.Errors.Should().BeEmpty();
    }
}
