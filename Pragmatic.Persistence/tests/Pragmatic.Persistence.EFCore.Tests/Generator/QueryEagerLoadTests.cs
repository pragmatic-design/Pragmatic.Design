using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     What a query brings back with the entity it answers with.
/// </summary>
/// <remarks>
///     <para>
///         <c>IIncludableQuery</c> and the executor's <c>Include</c> loop have been in place since
///         before this, and nothing generated implemented the interface — the only implementers in the
///         repository were one sample and four hand-written test classes. So the eager-loading path had
///         no producer: every generated query came back with its navigations empty, and
///         <c>[EagerLoad]</c>, which the mutation invoker reads, was inert on a query.
///     </para>
///     <para>
///         Only the entity-shaped result needs this. A query that answers with a DTO projects —
///         <c>filtered.Select(query.Projection)</c> — and EF turns a flattened path into a JOIN, so an
///         include there would add nothing.
///     </para>
/// </remarks>
public class QueryEagerLoadTests
{
    /// <param name="queryAttributes">What the query declares about its result and its loading.</param>
    /// <param name="resultType">The query's result type — the entity, or a DTO.</param>
    private static string Source(string queryAttributes, string resultType = "Order") => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Pragmatic.Actions.Mutation
        {
            // Declared rather than referenced: Pragmatic.Actions is not on this test project, and the
            // transform matches the attribute by name.
            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class EagerLoadAttribute : Attribute
            {
                public EagerLoadAttribute(string navigationPath) { }
            }
        }

        namespace TestApp
        {
            using Pragmatic.Actions.Mutation;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer : IEntity
            {
                public string Name { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
                public Guid CustomerId { get; private set; }
                public Customer Customer { get; private set; } = null!;
            }

            [MapFrom<Order>]
            public partial record OrderDto
            {
                public string Reference { get; init; } = "";

                [MapProperty("Customer.Name")]
                public string CustomerName { get; init; } = "";
            }

            {{queryAttributes}}
            [Query<Order, {{resultType}}>]
            public partial class FindOrdersQuery
            {
                [Filter(Operator = FilterOperator.Equals)]
                public string? Reference { get; init; }
            }
        }
        """;

    // ── What the author wrote ────────────────────────────────────────────────

    /// <summary>
    ///     <c>[EagerLoad]</c> on a query finally does something.
    /// </summary>
    [Fact]
    public void EagerLoadOnAQuery_IsHonoured()
    {
        var result = RunGenerator(Source("[EagerLoad(\"Customer\")]"));

        var generated = GetQuery(result);
        generated.Should().Contain("IIncludableQuery<global::TestApp.Order>");
        generated.Should().Contain("IncludePaths => [\"Customer\"]");
    }

    /// <summary>
    ///     A path that names no navigation is a build error, and is not emitted — it was an EF Core exception at the
    ///     first request.
    /// </summary>
    [Theory]
    [InlineData("Custmer", "Custmer")]
    [InlineData("Reference", "Reference")]
    [InlineData("Customer.Nickname", "Nickname")]
    public void APathThatNamesNoNavigation_IsReported_AndNotLoaded(string path, string segment)
    {
        var result = RunGenerator(Source($"[EagerLoad(\"{path}\")]"));

        result.RunResult.Diagnostics.Where(d => d.Id == "PRAG0736").Select(d => d.GetMessage())
            .Should().ContainSingle(m => m.Contains("FindOrdersQuery") && m.Contains($"'{segment}'"));
        GetQuery(result).Should().NotContain($"\"{path}\"", "a path EF Core would refuse is not handed to it");
    }

    /// <summary>The control: a path that names a navigation reports nothing.</summary>
    [Fact]
    public void APathThatNamesANavigation_ReportsNothing()
    {
        var result = RunGenerator(Source("[EagerLoad(\"Customer\")]"));

        result.RunResult.Diagnostics.Should().NotContain(d => d.Id == "PRAG0736");
    }

    // ── What is derived ──────────────────────────────────────────────────────

    /// <summary>
    ///     A query answering with the entity but declaring its response shape loads what that shape reaches.
    /// </summary>
    /// <remarks>
    ///     Not a second derivation: <c>RequiredNavigations</c> is the list Mapping already works out
    ///     from explicit paths, flattening, nested DTOs and collections.
    /// </remarks>
    [Fact]
    public void AResponseDto_DecidesWhatTheEntityBringsWithIt()
    {
        var result = RunGenerator(Source("[ReturnsDto<OrderDto>]"));

        var generated = GetQuery(result);
        generated.Should().Contain("IIncludableQuery<global::TestApp.Order>");
        generated.Should().Contain("OrderDto.RequiredNavigations");
    }

    /// <summary>
    ///     Declared paths come first because the author wrote them; the DTO's are appended.
    /// </summary>
    [Fact]
    public void DeclaredPathsAndDerivedOnes_AreBothLoaded()
    {
        var result = RunGenerator(Source("[EagerLoad(\"Customer\")]\n    [ReturnsDto<OrderDto>]"));

        GetQuery(result).Should().Contain("[\"Customer\", .. global::TestApp.OrderDto.RequiredNavigations]");
    }

    // ── What does not need it ────────────────────────────────────────────────

    /// <summary>
    ///     A query that projects needs no include: EF resolves the path in the database.
    /// </summary>
    [Fact]
    public void AProjectingQuery_IsLeftAlone()
    {
        var result = RunGenerator(Source("[EagerLoad(\"Customer\")]", resultType: "OrderDto"));

        var generated = GetQuery(result);
        generated.Should().NotContain("IIncludableQuery");
        generated.Should().Contain("Projection");
    }

    /// <summary>
    ///     And a query that declares nothing does not implement an interface it has nothing to say through.
    /// </summary>
    [Fact]
    public void AQueryThatDeclaresNothing_DoesNotImplementTheInterface()
    {
        var result = RunGenerator(Source(""));

        GetQuery(result).Should().NotContain("IIncludableQuery");
    }

    private static string GetQuery(SourceGenRunResult result)
    {
        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FindOrdersQuery.Query");
        generated.Should().NotBeNull("the query partial should be generated");
        return generated!;
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<IEntity>(),
                GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
                GeneratorTestHelper.FromType<EntityAttribute>(),
                GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
                GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
                GeneratorTestHelper.FromType<GenerateProjectionAttribute>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
            ]);
    }
}
