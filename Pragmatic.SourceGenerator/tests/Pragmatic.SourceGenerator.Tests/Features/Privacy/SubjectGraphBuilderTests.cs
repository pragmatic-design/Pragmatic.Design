using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Privacy.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Transforms;

namespace Pragmatic.SourceGenerator.Tests.Features.Privacy;

/// <summary>
///     Reachability from a data subject, which is what bounds the whole privacy analysis.
/// </summary>
public sealed class SubjectGraphBuilderTests
{
    private static PrivacyEntityModel Subject(string name) => new()
    {
        FullTypeName = name,
        TypeName = name,
        Namespace = string.Empty,
        SubjectIdentifier = "Id"
    };

    private static PrivacyEntityModel LinkedTo(string name, string? target) => new()
    {
        FullTypeName = name,
        TypeName = name,
        Namespace = string.Empty,
        SubjectPath = target is null ? null : "Path",
        SubjectPathTargetFullTypeName = target
    };

    [Fact]
    public void NoSubjectDeclared_NothingIsReachable()
    {
        // The property that makes the feature adoptable: a solution that has not opted in is silent,
        // however many entities and unclassified strings it has.
        var result = SubjectGraphBuilder.Reachable([LinkedTo("A", null), LinkedTo("B", null)]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ASubject_IsReachableFromItself()
        => SubjectGraphBuilder.Reachable([Subject("Customer")])
            .Should().ContainSingle().Which.FullTypeName.Should().Be("Customer");

    [Fact]
    public void DirectLink_IsReachable()
    {
        var result = SubjectGraphBuilder.Reachable([Subject("Customer"), LinkedTo("Order", "Customer")]);

        result.Select(e => e.FullTypeName).Should().BeEquivalentTo(["Customer", "Order"]);
    }

    [Fact]
    public void TransitiveLink_IsReachable()
    {
        var result = SubjectGraphBuilder.Reachable(
        [
            Subject("Customer"),
            LinkedTo("Order", "Customer"),
            LinkedTo("OrderLine", "Order")
        ]);

        result.Select(e => e.FullTypeName).Should().Contain("OrderLine");
    }

    [Fact]
    public void EntityWithNoPath_IsNotReachable()
    {
        var result = SubjectGraphBuilder.Reachable([Subject("Customer"), LinkedTo("AuditRow", null)]);

        result.Select(e => e.FullTypeName).Should().NotContain("AuditRow");
    }

    [Fact]
    public void PathToSomethingOutsideTheCompilation_IsNotReachable()
    {
        // The named target is not among the entities we can see, so nothing can be proven about it.
        var result = SubjectGraphBuilder.Reachable([Subject("Customer"), LinkedTo("Order", "SomeUnknownType")]);

        result.Select(e => e.FullTypeName).Should().NotContain("Order");
    }

    [Fact]
    public void CycleBetweenTwoEntities_TerminatesAndIsNotReachable()
    {
        // Two entities pointing at each other is an easy state to be in while paths are being wired up.
        // Hanging the IDE during that would be worse than reporting nothing yet.
        var result = SubjectGraphBuilder.Reachable(
        [
            Subject("Customer"),
            LinkedTo("A", "B"),
            LinkedTo("B", "A")
        ]);

        result.Select(e => e.FullTypeName).Should().BeEquivalentTo(["Customer"]);
    }

    [Fact]
    public void SelfReferencingEntity_Terminates()
    {
        var result = SubjectGraphBuilder.Reachable([Subject("Customer"), LinkedTo("A", "A")]);

        result.Select(e => e.FullTypeName).Should().BeEquivalentTo(["Customer"]);
    }

    [Fact]
    public void CycleThatEventuallyReachesASubject_IsStillReachable()
    {
        // Reaching a subject wins over the cycle: the walk stops at the subject, it does not keep going.
        var result = SubjectGraphBuilder.Reachable(
        [
            Subject("Customer"),
            LinkedTo("A", "B"),
            LinkedTo("B", "Customer")
        ]);

        result.Select(e => e.FullTypeName).Should().BeEquivalentTo(["Customer", "A", "B"]);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
        => SubjectGraphBuilder.Reachable([]).Should().BeEmpty();

    [Fact]
    public void TwoSeparateSubjects_BothGraphsAreReachable()
    {
        var result = SubjectGraphBuilder.Reachable(
        [
            Subject("Customer"),
            Subject("Employee"),
            LinkedTo("Order", "Customer"),
            LinkedTo("Payslip", "Employee")
        ]);

        result.Select(e => e.FullTypeName)
            .Should().BeEquivalentTo(["Customer", "Employee", "Order", "Payslip"]);
    }
}
