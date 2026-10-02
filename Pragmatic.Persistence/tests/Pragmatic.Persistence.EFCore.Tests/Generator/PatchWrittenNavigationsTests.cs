using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     What a patch says about the navigations it writes.
/// </summary>
/// <remarks>
///     <para>
///         A patch merges: it decides what to keep by looking at what is on the entity, so a child
///         collection nobody loaded is one it writes again — the same duplicate-per-element defect
///         measured on the mutation side, one flavour over. The caller of <c>ApplyPatch</c> is
///         hand-written code holding its own entity, and nothing loads for it, so the list has to be
///         readable from the type: pair it with <c>INavigationLoader.EnsureLoadedAsync</c>.
///     </para>
///     <para>
///         The member is named by callers that cannot see this model, so it is emitted on every patch
///         — empty list included. A missing member and an empty one are indistinguishable at the call
///         site, and only one of them compiles.
///     </para>
/// </remarks>
public class PatchWrittenNavigationsTests
{
    /// <param name="patchBody">The members the patch declares.</param>
    private static string Source(string patchBody) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Mapping.Mutation;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Patch;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order
        {
            public string Reference { get; set; } = "";
            public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class OrderLine
        {
            public string Description { get; set; } = "";
            public ICollection<LineTag> Tags { get; set; } = new List<LineTag>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class LineTag
        {
            public string Label { get; set; } = "";
        }

        [MapTo<LineTag>]
        public partial class LineTagDto
        {
            public Guid Id { get; init; }
            public string Label { get; init; } = "";
        }

        [MapTo<OrderLine>]
        public partial class OrderLineDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
            public List<LineTagDto> Tags { get; init; } = new();
        }

        [Patch<Order>]
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }

            {{patchBody}}
        }
        """;

    /// <summary>
    ///     The navigation the patch writes is published, and what its child writes comes with it.
    /// </summary>
    /// <remarks>
    ///     The deeper level is composed at runtime from the child's own list rather than walked here:
    ///     this transform sees one patch, and the grandchildren belong to another model. Naming the
    ///     child's static is safe because the same generator writes both in the same compilation.
    /// </remarks>
    [Fact]
    public void APatchThatWritesAChild_PublishesThatNavigation()
    {
        var result = RunGenerator(Source("public List<OrderLineDto> Lines { get; init; } = new();"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().Contain("WrittenNavigations");
        generated.Should().Contain("\"Lines\"");
        generated.Should().Contain("OrderLineDto.WrittenNavigations",
            "what the child writes in turn is what the caller still has to load, and only the child "
            + "knows it");
    }

    /// <summary>
    ///     A patch with nothing but scalars still publishes the member.
    /// </summary>
    [Fact]
    public void APatchWithNoChildren_PublishesAnEmptyList()
    {
        var result = RunGenerator(Source("public string? Note { get; init; }"));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().Contain("WrittenNavigations { get; } = [];");
    }

    /// <summary>
    ///     A collection declared <c>Ignore</c> is not written, so it is not something to load either.
    /// </summary>
    /// <remarks>
    ///     The discriminating case for the whole list: without it, a test that only ever sees written
    ///     children passes against an implementation that publishes every child it can see.
    /// </remarks>
    [Fact]
    public void ACollectionDeclaredIgnore_IsNotPublished()
    {
        var result = RunGenerator(Source(
            """
            [CollectionStrategy(CollectionStrategy.Ignore)]
            public List<OrderLineDto> Lines { get; init; } = new();
            """));

        NoCompilationErrors(result);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch");
        generated.Should().NotBeNull();
        generated!.Should().Contain("WrittenNavigations { get; } = [];",
            "a navigation the patch does not write is not one the caller has to load");
    }

    private static void NoCompilationErrors(SourceGenRunResult result)
    {
        var errors = GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("UpdateOrder.Patch") == true)
            .ToList();

        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
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
                GeneratorTestHelper.FromType<Patch.PatchAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                // The generated repositories and registrations are part of what has to compile: a
                // list that is right in a compilation missing half its references proves little.
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
                GeneratorTestHelper.FromType<Microsoft.Extensions.DependencyInjection.IServiceCollection>(),
                GeneratorTestHelper.FromTypeAssembly(
                    typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions)),
                GeneratorTestHelper.FromType<Microsoft.Extensions.Logging.ILogger>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Result.Result<,>)),
                GeneratorTestHelper.FromType<Result.IError>(),
                // A [Patch<T>] comes with its JSON converter, which names System.Text.Json.
                GeneratorTestHelper.FromTypeAssembly(typeof(System.Text.Json.JsonSerializer)),
            ]);
    }
}
