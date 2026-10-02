using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Actions.Tests.Generator;

/// <summary>
///     The <c>Include</c>s the invoker adds for what the children write in turn.
/// </summary>
/// <remarks>
///     <para>
///         The invoker includes every writable child, then names <c>{ChildDto}.WrittenNavigations</c>
///         to include the grandchildren that child writes too.
///     </para>
///     <para>
///         ⚠️ The list is <c>WrittenNavigations</c> and not <c>RequiredNavigations</c>, and the
///         difference is one of correctness. <c>RequiredNavigations</c> comes from the read model and is
///         emitted only on a <c>[MapFrom]</c>; <c>[MapTo]</c> is enough for a child to be writable. The
///         two conditions do not coincide, and a write-only child would produce
///         <c>CS0117: 'LineItemDto' does not contain a definition for 'RequiredNavigations'</c> inside a
///         file the author cannot open.
///     </para>
/// </remarks>
public class MutationDeepIncludeTests : ActionsGeneratorTestBase
{
    private const string EfCorePresence = """
        namespace Pragmatic.Persistence.EFCore
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class PragmaticDbContextAttribute : Attribute { }
        }
        """;

    /// <param name="childDtoAttributes">What the child DTO declares about itself.</param>
    private static string Source(string childDtoAttributes) => $$"""
        using System;
        using System.Collections.Generic;
        using Pragmatic.Actions.Mutation;
        using Pragmatic.Mapping.Attributes;
        using Pragmatic.Persistence.Entity;

        {{EfCorePresence}}

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [BelongsTo<SalesBoundary>]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
            public ICollection<LineItem> Lines { get; set; } = new List<LineItem>();
        }

        [Entity]
        [BelongsTo<SalesBoundary>]
        [PartOf<Order>]
        public partial class LineItem : IEntity
        {
            public string Description { get; set; } = "";
        }

        {{childDtoAttributes}}
        public partial class LineItemDto
        {
            public Guid Id { get; init; }
            public string Description { get; init; } = "";
        }

        [Mutation(Mode = MutationMode.Update)]
        public partial class UpdateOrderMutation : Mutation<Order>
        {
            public Guid Id { get; init; }
            public List<LineItemDto> Lines { get; init; } = new();
        }
        """;

    /// <summary>A bidirectional child: the invoker names its write list, and it compiles.</summary>
    [Fact]
    public void AChildThatAlsoMapsFrom_HasItsWrittenNavigationsIncluded()
    {
        var result = RunGeneratorForMutation(Source("[MapFrom<LineItem>]\n[MapTo<LineItem>]"));

        var invoker = GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("LineItemDto.WrittenNavigations",
            "the invoker also loads what the child writes in turn");

        ErrorsIn(result, "MutationInvoker").Should().BeEmpty();
    }

    /// <summary>
    ///     ⚠️ A <b>write-only</b> child: same treatment, and it compiles.
    /// </summary>
    /// <remarks>
    ///     <c>WrittenNavigations</c> is emitted by every <c>[MapTo]</c>, which is the same condition
    ///     that makes a child writable: the two coincide by construction, not by luck.
    /// </remarks>
    [Fact]
    public void AWriteOnlyChild_HasItsWrittenNavigationsIncluded()
    {
        var result = RunGeneratorForMutation(Source("[MapTo<LineItem>]"));

        var invoker = GetGeneratedSource(result, "UpdateOrderMutation.MutationInvoker");
        invoker.Should().NotBeNull();
        invoker!.Should().Contain("LineItemDto.WrittenNavigations",
            "a child without [MapFrom] is writable like any other, so it is loaded like any other");

        var errors = ErrorsIn(result, "MutationInvoker");
        errors.Should().BeEmpty(string.Join(" | ", errors));
    }

    /// <summary>The list the invoker names really exists on the child DTO, in both cases.</summary>
    [Theory]
    [InlineData("[MapFrom<LineItem>]\n[MapTo<LineItem>]")]
    [InlineData("[MapTo<LineItem>]")]
    public void AWritableChild_PublishesWrittenNavigations(string childDtoAttributes)
    {
        var result = RunGeneratorForMutation(Source(childDtoAttributes));

        var child = GetGeneratedSource(result, "LineItemDto.Mapping");
        child.Should().NotBeNull();
        child!.Should().Contain("public static IReadOnlyList<string> WrittenNavigations",
            "it is emitted by every [MapTo], empty list included: whoever names it cannot check it");
    }

    private static List<string> ErrorsIn(SourceGenRunResult result, string hint)
        => GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree?.FilePath.Contains(hint) == true)
            .Select(d => d.ToString())
            .ToList();

    private static SourceGenRunResult RunGeneratorForMutation(string source)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
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
