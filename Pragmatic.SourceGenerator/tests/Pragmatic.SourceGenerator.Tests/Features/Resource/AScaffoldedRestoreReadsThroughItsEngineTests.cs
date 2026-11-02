using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Resource;

/// <summary>
///     The restore a <c>[Resource]</c> scaffolds for a soft-delete entity reads its row the way its engine does: on the Sql or documents engine
///     through the repository with the soft-delete filter lifted by the toggle, not through EF Core's <c>IgnoreQueryFilters</c> on <c>Query()</c>,
///     which a compilation without EF Core does not have.
/// </summary>
/// <remarks>
///     A hand-written restore had the engine branch already. The scaffolded one went to the invoker template without passing the engine
///     resolution the hand-written ones get, so its engine was unknown and it took EF Core's branch. The Sql engine's bridge is declared by the
///     type the generator recognises it by, as in <c>PersistenceEngineChoiceTests</c>.
/// </remarks>
public class AScaffoldedRestoreReadsThroughItsEngineTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private const string Source = """
        using System;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Showcase.Booking
        {
            [Boundary]
            public partial class BookingBoundary;
        }

        namespace Showcase.Booking.Entities
        {
            [Entity]
            [SoftDelete]
            [Resource("templates", Capabilities = ResourceCapabilities.Delete | ResourceCapabilities.Restore)]
            public partial class Template : IEntity
            {
                public string Name { get; private set; } = "";
            }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.IEntity>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.Entity.ResourceAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Actions.Mutation.Mutation<>)),
        GeneratorTestHelper.FromType<Pragmatic.Result.IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Pragmatic.Result.Result<,>)),
    ];

    private static string TheRestoreInvoker(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(kv => kv.Key.Contains("RestoreTemplateMutation") && kv.Key.Contains("MutationInvoker")).Value;

    [Fact]
    public void OnTheSqlEngine_TheRestoreLiftsTheFilterThroughTheToggle()
    {
        var invoker = TheRestoreInvoker(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source + SqlBridge, References));

        invoker.Should().NotContain("Microsoft.EntityFrameworkCore", "the compilation has no EF Core: the engine reads the row");
        invoker.Should().Contain("_filterToggle?.DisableQueryFilter(\"SoftDelete\")");
        invoker.Should().Contain("_repository.GetByIdAsync(mutation.Id, ct)");
    }

    /// <summary>The control: with EF Core the only engine, the scaffolded restore reads as it always did.</summary>
    [Fact]
    public void OnEfCore_TheRestoreIgnoresTheNamedFilter()
    {
        var invoker = TheRestoreInvoker(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source,
            [.. References, GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()]));

        invoker.Should().Contain("IgnoreQueryFilters(_repository.Query(), new[] { \"SoftDelete\" })");
    }
}
