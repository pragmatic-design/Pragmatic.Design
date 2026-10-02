using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     A DTO that carries both <c>[MapTo&lt;T&gt;]</c> and <c>[Patch&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Not a forbidden pair: the patch template reads <c>HasMapToAttribute</c> precisely so that
///         <c>ApplyPatch</c> can delegate to <c>ApplyTo</c>. But both generators published
///         <c>WrittenNavigations</c> on the same partial class, in two <c>.g.cs</c> files, and the
///         author got <c>CS0102</c> inside a file they cannot open. Mapping already keeps quiet on a
///         mutation body, because Actions composes the list there; it now keeps quiet on a patch for
///         the same reason.
///     </para>
///     <para>
///         Runs the whole generator and compiles the result: a duplicate member is a compiler error,
///         and only the compiler can say it.
///     </para>
/// </remarks>
public class PatchWithMapToTests
{
    /// <param name="dtoAttributes">The attributes on the DTO under test.</param>
    private static string Source(string dtoAttributes) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Mapping.Attributes;
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
        }

        [MapTo<OrderLine>]
        public partial class OrderLineDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }

        {{dtoAttributes}}
        public partial class UpdateOrder
        {
            public string? Reference { get; init; }
            public List<OrderLineDto> Lines { get; init; } = new();
        }
        """;

    /// <summary>
    ///     ⚠️ The case at risk of a duplicate member: one list, published by the side that composes it.
    /// </summary>
    [Fact]
    public void MapToAndPatchOnOneType_Compile_AndThePatchOwnsTheList()
    {
        var result = RunGenerator(Source("[MapTo<Order>]\n[Patch<Order>]"));

        ErrorsInTheDtoFiles(result)
            .Should().NotContain(d => d.Id == "CS0102",
                "two generators publishing the same member on one partial class is the defect");
        ErrorsInTheDtoFiles(result).Should().BeEmpty();

        GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Patch")!
            .Should().Contain("WrittenNavigations", "the patch composes the children's lists, so its is the informed one");
        GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Mapping")!
            .Should().NotContain("WrittenNavigations", "and Mapping keeps quiet, as it does on a mutation body");
    }

    /// <summary>
    ///     The control: <c>[MapTo]</c> alone still publishes the list from Mapping.
    /// </summary>
    /// <remarks>
    ///     Without it, a Mapping that never emitted the list would pass the case above — and the
    ///     mutation invoker, which names the member for every writable child, would stop compiling.
    /// </remarks>
    [Fact]
    public void MapToAlone_StillPublishesTheListFromMapping()
    {
        var result = RunGenerator(Source("[MapTo<Order>]"));

        ErrorsInTheDtoFiles(result).Should().BeEmpty();

        GeneratorTestHelper.GetGeneratedSource(result, "UpdateOrder.Mapping")!
            .Should().Contain("WrittenNavigations");
    }

    /// <summary>
    ///     The errors in the two files under test. The repositories and registrations the generator
    ///     also emits need a reference closure this harness does not carry — the restriction
    ///     <c>PatchApplyCollectionTests</c> works under too — and a duplicate member is reported in the
    ///     file that declares it second, which is one of these two.
    /// </summary>
    private static List<Diagnostic> ErrorsInTheDtoFiles(SourceGenRunResult result)
    {
        return GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains("UpdateOrder.") == true)
            .ToList();
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
                // A [MapTo] compiled next to EF Core gets the load-aware ApplyTo(entity, context),
                // which names the EF-side helpers.
                GeneratorTestHelper.FromTypeAssembly(typeof(Mapping.EFCore.Mutation.EfMutationHelpers)),
                GeneratorTestHelper.FromType<Patch.PatchAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
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
