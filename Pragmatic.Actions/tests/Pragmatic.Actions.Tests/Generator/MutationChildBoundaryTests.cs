using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     Where nesting stops: at the boundary.
/// </summary>
/// <remarks>
///     <para>
///         A boundary is the <b>transaction</b> boundary: each one saves from its own
///         <c>DbContext</c>. A nested child across the boundary would be committed by the parent's unit
///         of work, past every rule the owning boundary declares on its own rows.
///     </para>
///     <para>
///         The <c>DbSet</c> that <c>[ReadAccess&lt;T&gt;]</c> adds is writable like any other —
///         <c>ExcludeFromMigrations</c> is about the schema, not about permissions — so the refusal must
///         come from somewhere else: the boundary's <c>SaveChanges</c> at runtime
///         (<c>ReadAccessAcrossTheBoundary</c>), and <c>PRAG0444</c> at compile time.
///     </para>
///     <para>
///         In a file of its own because <c>MutationChildrenTests</c> is already long.
///     </para>
/// </remarks>
public class MutationChildBoundaryTests
{
    /// <summary>
    ///     ⚠️ A child across the boundary is refused.
    /// </summary>
    /// <remarks>
    ///     The relation is legitimate — an <c>Order</c> can point to a <c>CatalogItem</c>, and
    ///     <c>conformance</c> declares that shape on purpose. What is refused is <b>writing</b> it, and
    ///     <c>PRAG0444</c> is what names it.
    /// </remarks>
    [Fact]
    public void AChildOfAnotherBoundary_IsRefused()
    {
        GeneratorTestHelper
            .HasDiagnostic(RunWith(childBoundary: "CatalogBoundary"), "PRAG0444")
            .Should().BeTrue("the child would be written by the parent's unit of work");

        GeneratorTestHelper
            .HasDiagnostic(RunWith(childBoundary: "SalesBoundary"), "PRAG0444")
            .Should().BeFalse("while the very same shape inside a single boundary is correct");
    }

    /// <param name="childBoundary">The boundary the child declares it belongs to.</param>
    private static SourceGenRunResult RunWith(string childBoundary) => RunGenerator($$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Persistence.Entity;

        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }

        namespace TestApp;

        public class SalesBoundary;
        public class CatalogBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Reference { get; set; } = "";
            public CatalogItem? Item { get; set; }
        }

        [Entity]
        [BelongsTo<{{childBoundary}}>]
        public partial class CatalogItem : IEntity
        {
            public string Sku { get; set; } = "";
        }

        [Mutation(Mode = MutationMode.Update, Internal = true)]
        public partial class WriteItemMutation : Mutation<CatalogItem>
        {
            public Guid Id { get; init; }
            public string Sku { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public WriteItemMutation? Item { get; init; }
        }
        """);

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            [
                GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.DomainActionAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
                GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Ensure.Ensure)),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
                GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.SoftDeleteAttribute>(),
                GeneratorTestHelper.FromType<Pragmatic.Mapping.Attributes.MapToAttribute<object>>(),
                GeneratorTestHelper.FromType<Pragmatic.Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
            ]);
    }
}
