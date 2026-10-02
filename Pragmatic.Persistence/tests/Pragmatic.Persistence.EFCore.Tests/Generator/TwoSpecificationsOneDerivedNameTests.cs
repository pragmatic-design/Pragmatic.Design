using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Two specifications that would derive the same query type are refused, not collided.
/// </summary>
/// <remarks>
///     <para>
///         The derived type is <c>{member}Query</c> in the container's namespace, so two specification
///         classes sharing a namespace and a member name — <c>OrderSpecs.Pending()</c> and
///         <c>ShipmentSpecs.Pending()</c> in one namespace — both derive <c>PendingQuery</c> there.
///     </para>
///     <para>
///         ⚠️ The consequence is not a duplicate type the author can see. Both outputs carry the same
///         hint name, and Roslyn answers a duplicate hint by <b>discarding the whole generator's
///         output</b> with a <c>CS8785</c> that is only a <em>warning</em>: without
///         <c>--warnaserror</c> the build succeeds with every generated file missing. That failure mode
///         is why this is worth a diagnostic of its own rather than being left to the compiler.
///     </para>
///     <para>
///         The issue that introduced the promotion named this as its one unresolved question. The answer
///         chosen is to refuse it: renaming every derived type to carry its container would change a
///         public name for everybody to avoid a case that is rare and, said out loud, ambiguous anyway —
///         two rules with one name in one namespace.
///     </para>
/// </remarks>
public class TwoSpecificationsOneDerivedNameTests
{
    private const string Entity = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;
        using Pragmatic.Specification;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";
            public bool IsPending { get; private set; }
        }
        """;

    [Fact]
    public void TwoSpecificationsDerivingOneName_AreReported()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>]
                public static Specification<Order> Pending()
                    => Spec<Order>.Where(o => o.IsPending);
            }

            public static class ShipmentSpecs
            {
                [Query<Order>]
                public static Specification<Order> Pending()
                    => Spec<Order>.Where(o => !o.IsPending);
            }
            """);

        // The ids are in the message on purpose: "no PRAG0726" and "the generator fell over somewhere
        // else" look identical from a boolean, and the second is what a duplicate hint actually does.
        var ids = string.Join(", ", result.Diagnostics.Select(d => d.Id).Distinct());

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0726").Should().BeTrue(
            "both derive PendingQuery in one namespace, and a duplicate hint costs the entire "
            + "generator's output under a warning nobody reads — diagnostics were: [{0}]", ids);

        var reported = GeneratorTestHelper.GetDiagnosticsById(result, "PRAG0726").ToList();
        reported.Should().NotBeEmpty();
        reported[0].GetMessage().Should().Contain("PendingQuery",
            "the message names the type they collide on, which is what the author has to change");
    }

    /// <summary>
    ///     The control: the same two names in different namespaces derive two queries and say nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "refuse a collision" and "refuse two specifications with the same member name"
    ///     are the same green — and the second would refuse an ordinary shape, since a namespace per
    ///     resource folder is exactly how this repository lays modules out.
    /// </remarks>
    [Fact]
    public void TheSameNameInTwoNamespaces_IsNotACollision()
    {
        var result = Run(Entity + """

            namespace TestApp.Orders
            {
                public static class OrderSpecs
                {
                    [Query<Order>]
                    public static Specification<Order> Pending()
                        => Spec<Order>.Where(o => o.IsPending);
                }
            }

            namespace TestApp.Shipments
            {
                public static class ShipmentSpecs
                {
                    [Query<Order>]
                    public static Specification<Order> Pending()
                        => Spec<Order>.Where(o => !o.IsPending);
                }
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0726").Should().BeFalse(
            "the hint carries the namespace, so these two do not collide");

        var generated = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        generated.Keys.Count(k => k.Contains("PendingQuery.DerivedQuery")).Should().Be(2,
            "both are derived, each in its own namespace");
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, References());

    private static MetadataReference[] References() =>
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
        GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
        GeneratorTestHelper.FromType<Specification.Specification<object>>(),
        GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
    ];
}
