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
///     A specification carries the rule; <c>[Query]</c> on it publishes the rule as a read.
/// </summary>
/// <remarks>
///     <para>
///         Writing the same rule twice was the only way to make it readable: the specification held the
///         predicate, and a second class beside it restated the predicate, added paging by hand and
///         declared the route. <c>[Query]</c> could not sit on the specification because it was
///         <c>AttributeTargets.Class</c> and a specification is, in practice, a static factory.
///     </para>
///     <para>
///         ⚠️ The derived type is a <b>new</b> type. Adding <c>Page</c>/<c>PageSize</c> to the
///         specification would destroy what makes it worth having — <c>Confirmed() &amp; OfKind(x)</c>
///         has no answer for which page it is on — so the predicate stays a predicate and the query
///         holds it as a property.
///     </para>
/// </remarks>
public class SpecificationPromotedToQueryTests
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
            public bool IsConfirmed { get; private set; }
        }
        """;

    /// <summary>A specification without parameters becomes a query with none.</summary>
    [Fact]
    public void AParameterlessSpecification_BecomesAQuery()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        // ⚠️ First: the attribute has to be legal there at all. Without AttributeTargets.Method this is
        // CS0592 on the author's own line, and the generation below happens anyway — a generator does not
        // stop for an attribute-usage error, so asserting only on the output would measure nothing.
        // Narrowed to the author's file: this fixture declares the minimum the derivation needs, and the
        // generated repository registration wants references it does not carry.
        GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.ToString().Contains("TestSource"))
            .Should().BeEmpty();

        var derived = GeneratorTestHelper.GetGeneratedSource(result, "ConfirmedQuery.DerivedQuery");

        derived.Should().NotBeNull("the query type is derived from the specification");
        derived!.Should().Contain("public partial class ConfirmedQuery");
        derived.Should().Contain("Rule",
            "the specification is held as a property, which is how Apply() reaches it");
        derived.Should().Contain("global::TestApp.OrderSpecs.Confirmed()",
            "and the rule stays where it was written: the query calls it");
    }

    /// <summary>The specification's parameters become the query's inputs.</summary>
    /// <remarks>
    ///     It is also how a parameterised rule gets its value from the caller: there is nowhere else for
    ///     <c>ConfirmedTerm(string term)</c> to read <c>term</c> from.
    /// </remarks>
    [Fact]
    public void TheSpecificationsParameters_BecomeTheQuerysInputs()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>(Paged = true)]
                public static Specification<Order> WithCode(string code)
                    => Spec<Order>.Where(o => o.Code == code);
            }
            """);

        var derived = GeneratorTestHelper.GetGeneratedSource(result, "WithCodeQuery.DerivedQuery");

        derived.Should().NotBeNull();
        derived!.Should().Contain("public string Code { get; init; }");
        derived.Should().Contain("global::TestApp.OrderSpecs.WithCode(Code)",
            "the input is passed to the rule, not turned into a second filter beside it");

        derived.Should().Contain("IPagedInput", "Paged = true generates the paging surface");
        derived.Should().Contain("public int Page { get; init; } = 1;");
        derived.Should().Contain("public int PageSize { get; init; } = 20;");
    }

    /// <summary>
    ///     And <c>Apply()</c> comes from the ordinary templates, which is the test of the derivation.
    /// </summary>
    /// <remarks>
    ///     The derived type is generated so that the templates serving a hand-written query serve it
    ///     unchanged. A derived query that needed a renderer of its own would not be a query.
    /// </remarks>
    [Fact]
    public void TheDerivedQuery_IsRenderedByTheOrdinaryTemplates()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        var apply = GeneratorTestHelper.GetGeneratedSource(result, "ConfirmedQuery.Query");

        apply.Should().NotBeNull("the query's own Apply/ToSpecification file is emitted for it too");
        apply!.Should().Contain("Apply(");
        apply.Should().Contain("Rule", "and Apply filters by the specification the query holds");
    }

    /// <summary>The control: the specification itself is untouched.</summary>
    /// <remarks>
    ///     Nothing is written into the declaring type, and the predicate gains no paging member — which
    ///     is what keeps it composable with <c>&amp;</c>.
    /// </remarks>
    [Fact]
    public void TheSpecificationItself_IsUnchanged()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>(Paged = true)]
                public static Specification<Order> Confirmed()
                    => Spec<Order>.Where(o => o.IsConfirmed);
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "OrderSpecs").Should().BeNull(
            "the promotion derives a type; it does not write into the one that holds the rule");
    }

    /// <summary>And the second control: a static member that is not a specification derives nothing.</summary>
    [Fact]
    public void AMemberThatIsNotASpecification_DerivesNothing()
    {
        var result = Run(Entity + """

            public static class OrderSpecs
            {
                [Query<Order>]
                public static string Describe() => "an order";
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "DescribeQuery").Should().BeNull();
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
