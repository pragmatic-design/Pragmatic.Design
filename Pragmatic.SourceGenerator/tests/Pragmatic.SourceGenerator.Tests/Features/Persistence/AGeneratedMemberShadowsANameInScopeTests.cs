using System;
using System.Linq;
using Pragmatic.SourceGenerator.Tests.Features.Traits;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A computed body names a navigation the relation generates, and the name is also a namespace or a
///     type in scope: the body means the entity's member, and so does its rewrite.
/// </summary>
/// <remarks>
///     <para>
///         While the transform runs the generated member does not exist, so the name binds to whatever
///         else carries it. In the entity's own code the member wins — C# looks in the type before the
///         namespaces around it — but the rewrite recognised a generated member only when the name bound
///         to <b>nothing</b>, and left it bare. In the generated class the bare name is the namespace, or
///         the type.
///     </para>
///     <para>
///         ⚠️ Both shapes are ordinary. A module's folders are namespaces named after its resources, so
///         <c>Employee.LeaveRequests</c> meets <c>TimeOff.Leave.LeaveRequests</c>; and a navigation named
///         after its type — <c>Customer</c> of type <c>Customer</c> — is the default a relation writes.
///     </para>
/// </remarks>
public class AGeneratedMemberShadowsANameInScopeTests
{
    private const string Source = """
        using System;
        using System.Linq;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.EFCore;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Shop.Lines
        {
            public static class Nothing { }
        }

        namespace Shop.Entities
        {
            public sealed class ShopBoundary { }

            public enum OrderState { Open, Closed }

            [Entity]
            [BelongsTo<ShopBoundary>]
            public partial class Customer : IEntity
            {
                public string Name { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<ShopBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.OneToMany<Line>.WithNavigation("Lines", Inverse = "Order")]
            public partial class Order : IEntity
            {
                public OrderState State { get; private set; }

                [ComputedFilter]
                public bool HasLines => Lines.Any();

                [Projectable]
                public string CustomerName => Customer.Name;

                [ComputedFilter]
                public bool IsOpen => State == OrderState.Open;
            }

            [Entity]
            [BelongsTo<ShopBoundary>]
            public partial class Line : IEntity
            {
                public decimal Amount { get; private set; }
            }

            [PragmaticDbContext("Shop")]
            public partial class ShopDbContext { }
        }
        """;

    /// <summary>A navigation that shares its name with a namespace is read from the row.</summary>
    [Fact]
    public void ANavigationNamedLikeANamespace_IsTheRowsMember()
    {
        Generated("Order.ComputedFilter").Should().Contain("e.Lines.Any()");
    }

    /// <summary>A navigation named after its type is read from the row, not taken for the type.</summary>
    [Fact]
    public void ANavigationNamedLikeItsType_IsTheRowsMember()
    {
        Generated("Order.Projectable").Should().Contain("e.Customer.Name");
    }

    /// <summary>The control: a type the body really names — an enum — is still the type, qualified.</summary>
    [Fact]
    public void ATypeTheBodyNames_IsStillTheType()
    {
        Generated("Order.ComputedFilter").Should().Contain("global::Shop.Entities.OrderState.Open");
    }

    /// <summary>And what the generator writes compiles.</summary>
    [Fact]
    public void ItCompiles()
    {
        var (errors, _) = TraitCompilationHarness.CompileAndSplitErrors(
            Source, file => file.Contains("Order.ComputedFilter", StringComparison.Ordinal)
                            || file.Contains("Order.Projectable", StringComparison.Ordinal));

        errors.Should().BeEmpty(TraitCompilationHarness.FormatErrors(errors));
    }

    private static string Generated(string hintPart)
    {
        var (sources, _) = TraitCompilationHarness.Generate(Source);
        var generated = sources.FirstOrDefault(s => s.Key.Contains(hintPart)).Value;
        generated.Should().NotBeNull($"{hintPart} is generated at all — otherwise every assertion is about nothing");
        return generated!;
    }
}
