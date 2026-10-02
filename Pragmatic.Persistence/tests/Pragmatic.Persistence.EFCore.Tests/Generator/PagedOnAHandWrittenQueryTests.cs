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
///     <c>Paged = true</c> writes the paging surface on a declared query, not only on a derived one.
/// </summary>
/// <remarks>
///     <para>
///         The option is declared on both arities of <c>[Query]</c> and its documentation says the four
///         lines it saves are the ones written out today on a hand-written query. Only the derived path
///         read it, so on a declared class the option was accepted and did nothing: the query kept
///         answering every matching row, which is the shape it had before the option existed.
///     </para>
///     <para>
///         ⚠️ That is the failure this repository keeps finding — a declaration with no reader. It is
///         worse here than a missing feature, because the author who writes it has said what they want
///         and the build agrees with them.
///     </para>
/// </remarks>
public class PagedOnAHandWrittenQueryTests
{
    private const string Entity = """
        using System;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";
        }
        """;

    [Fact]
    public void AQueryAskingForPaging_GetsThePagingSurface()
    {
        var result = Run(Entity + """

            [Query<Order>(Paged = true)]
            public partial class ListOrdersQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Code { get; init; }
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().Contain("public int Page { get; init; } = 1;",
            "the option's whole purpose is to stop the author writing these two lines");
        generated.Should().Contain("public int PageSize { get; init; } = 20;");
        generated.Should().Contain("IPagedQuery<",
            "and the query has to satisfy the interface the executor overload takes, or the paging "
            + "properties are two fields nobody reads");
    }

    /// <summary>The control: a query that says nothing gets nothing.</summary>
    /// <remarks>
    ///     Without it "the surface is generated" would be satisfied by generating it everywhere, which
    ///     would turn every list query into a paged one and change what it answers.
    /// </remarks>
    [Fact]
    public void AQueryNotAskingForIt_GetsNoPaging()
    {
        var result = Run(Entity + """

            [Query<Order>]
            public partial class ListOrdersQuery
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Code { get; init; }
            }
            """);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "ListOrdersQuery.Query");

        generated.Should().NotBeNull();
        generated!.Should().NotContain("public int Page { get; init; }");
        generated.Should().Contain("IQuery<");
        generated.Should().NotContain("IPagedQuery<");
    }

    /// <summary>
    ///     The second control: a query that already pages by hand is not given a second copy.
    /// </summary>
    /// <remarks>
    ///     Two declarations of <c>Page</c> in one partial class is <c>CS0102</c>, in a file the author
    ///     cannot edit. The option is redundant there and says so — <c>PRAG0727</c> — rather than being
    ///     ignored, because an option that is silently ignored teaches the author nothing.
    /// </remarks>
    [Fact]
    public void AQueryDeclaringItsOwnPaging_IsNotGivenASecondCopy()
    {
        var result = Run(Entity + """

            [Query<Order>(Paged = true)]
            public partial class ListOrdersQuery
            {
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
            }
            """);

        DuplicateMemberErrors(result).Should().BeEmpty(
            "the generated half must not declare a member the author already wrote");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0727").Should().BeTrue(
            "the option adds nothing here, and saying so is the difference between a redundant "
            + "declaration and one the author believes is doing something");
    }

    /// <summary>
    ///     The <c>CS0102</c> errors: a member declared twice in one type.
    /// </summary>
    /// <remarks>
    ///     Named rather than asserting the compilation is clean, and deliberately so: this fixture
    ///     carries the minimum a query needs to be <em>generated</em>, not to compile, so the invoker,
    ///     the repository and the setters all name assemblies it does not reference. Asserting "no
    ///     errors" there would fail on the fixture's shape and say nothing about this rule.
    ///     <c>CS0102</c> is exactly the failure a second <c>Page</c> produces, and it is reported in
    ///     the half the author cannot edit.
    /// </remarks>
    private static string[] DuplicateMemberErrors(SourceGenRunResult result)
        => [.. GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Id == "CS0102")
            .Select(d => d.ToString())];

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
