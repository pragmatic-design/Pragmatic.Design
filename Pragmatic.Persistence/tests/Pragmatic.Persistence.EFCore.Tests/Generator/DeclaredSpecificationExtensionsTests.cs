using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A specification declared once reaches the two places it is consumed.
/// </summary>
/// <remarks>
///     <para>
///         A <c>Specification&lt;TEntity&gt;</c> is written once and then used in its most awkward
///         form: <c>_items.Query().Where(KnowledgeSpecs.Confirmed().ToExpression())</c> names three
///         things to say one, and teaches the next reader that <c>.ToExpression()</c> is what one
///         writes.
///     </para>
///     <para>
///         The generator already knows what a specification is — <c>QuerySpecificationModel</c>
///         recognises the type <b>deliberately, rather than by an attribute</b>, because "the type
///         already says what the property is". That knowledge stopped at the boundaries of a
///         <c>[Query]</c> class: a specification declared on its own reached no generated surface.
///     </para>
///     <para>
///         ⚠️ Nothing failed. The hand-written form compiles, runs and produces the right SQL — there
///         is no signal for «the framework could have written this», which is why it stayed.
///     </para>
/// </remarks>
public class DeclaredSpecificationExtensionsTests
{
    private const string Source = """
        using System;
        using Pragmatic.Persistence.Entity;
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

        public static class OrderSpecs
        {
            public static Specification<Order> Confirmed()
                => Spec<Order>.Where(o => o.IsConfirmed);

            public static Specification<Order> WithCode(string code)
                => Spec<Order>.Where(o => o.Code == code);
        }
        """;

    /// <summary>The queryable form, with and without parameters.</summary>
    [Fact]
    public void TheDeclaredSpecification_BecomesAQueryableExtension()
    {
        var generated = GeneratedExtensions(Source);

        generated.Should().NotBeNull();
        generated!.Should().Contain(
            "Confirmed(this global::System.Linq.IQueryable<global::TestApp.Order> query)",
            "a specification without parameters filters a queryable without any either");
        generated.Should().Contain(
            "WithCode(this global::System.Linq.IQueryable<global::TestApp.Order> query, string code)",
            "and the specification's own parameters become the extension's");
    }

    /// <summary>And the four terminal forms the repository already offers, bound to the name.</summary>
    [Fact]
    public void TheDeclaredSpecification_BecomesTheRepositoryTerminals()
    {
        var generated = GeneratedExtensions(Source);

        generated.Should().NotBeNull();
        generated!.Should().Contain("FindConfirmedAsync(");
        generated.Should().Contain("CountConfirmedAsync(");
        generated.Should().Contain("AnyConfirmedAsync(");
        generated.Should().Contain("FirstConfirmedOrDefaultAsync(");

        generated.Should().Contain("repository.FindAsync(global::TestApp.OrderSpecs.Confirmed(), ct)",
            "the terminal delegates to the member the interface already has, with the named specification");
    }

    /// <summary>It compiles: the extensions name types this compilation has.</summary>
    [Fact]
    public void TheGeneratedExtensions_Compile()
    {
        var result = Run(Source);

        var errors = string.Join(" | ", GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.ToString().Contains("SpecificationExtensions"))
            .Select(d => d.ToString()));

        errors.Should().BeEmpty();
    }

    /// <summary>
    ///     The control: a static member that does not return a specification generates nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, «recognised by type» would also be satisfied by recognition by name or by being
    ///     static — which would generate extensions for half of an application's code.
    /// </remarks>
    [Fact]
    public void AStaticMemberThatIsNotASpecification_GeneratesNothing()
    {
        var generated = GeneratedExtensions("""
            using System;
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Specification;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public string Code { get; private set; } = "";
            }

            public static class OrderHelpers
            {
                public static string Describe() => "an order";

                public static Func<Order, bool> Confirmed() => _ => true;
            }
            """);

        generated.Should().BeNull("neither member is a Specification<T>");
    }

    private static string? GeneratedExtensions(string source)
        => GeneratorTestHelper.GetGeneratedSource(Run(source), "Specifications.TestApp.Order");

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
