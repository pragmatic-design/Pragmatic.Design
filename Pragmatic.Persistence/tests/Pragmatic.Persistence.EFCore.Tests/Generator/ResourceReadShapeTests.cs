using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     The scaffolded read shape is emitted whenever something answers with it, not only when the
///     resource can be read.
/// </summary>
/// <remarks>
///     <para>
///         A write answers with the read shape while the resource still exists — a create with what it
///         created, an update and a restore with what they left behind, a soft delete with the row it
///         marked. <c>ReadShapeOperations</c> says exactly that. The emission was guarded by
///         <c>Capabilities</c> containing <c>Read</c> as well, so a resource that only creates, or only
///         deletes, named a DTO nobody generated — a build error inside a file its author cannot open.
///     </para>
///     <para>
///         Found while making <c>RoomType</c> a partially scaffolded resource, and it is the shape this
///         codebase keeps meeting: a fact computed where only one of its two consumers can see it.
///     </para>
/// </remarks>
public class ResourceReadShapeTests
{
    /// <param name="capabilities">The capability expression the resource declares.</param>
    private static string Source(string capabilities) => $$"""
        using System;
        using Pragmatic.Persistence.Entity;

        namespace TestApp;

        public class SalesBoundary;

        [Entity]
        [SoftDelete]
        [BelongsTo<SalesBoundary>]
        [Resource("orders", Capabilities = {{capabilities}})]
        public partial class Order : IEntity
        {
            public string Reference { get; private set; } = "";
        }
        """;

    /// <summary>
    ///     A soft delete answers with the row it marked, so the shape it answers with has to exist.
    /// </summary>
    [Fact]
    public void DeleteAlone_StillGetsTheShapeItAnswersWith()
    {
        var result = RunGenerator(Source("ResourceCapabilities.Delete"));

        SomethingNames(result, "OrderReadDto").Should().BeTrue(
            "the soft delete answers with the read shape, so the generated code names it");
        GeneratorTestHelper.GetGeneratedSource(result, "Order.ReadDto").Should().NotBeNull(
            "and naming a type nobody emits is a build error in a file the author cannot open");
    }

    /// <summary>
    ///     And so does a create, which is the more likely declaration of the two.
    /// </summary>
    [Fact]
    public void CreateAlone_StillGetsTheShapeItAnswersWith()
    {
        var result = RunGenerator(Source("ResourceCapabilities.Create"));

        SomethingNames(result, "OrderReadDto").Should().BeTrue();
        GeneratorTestHelper.GetGeneratedSource(result, "Order.ReadDto").Should().NotBeNull();
    }

    /// <summary>
    ///     Nothing answers with the read shape here, so nothing is written.
    /// </summary>
    /// <remarks>
    ///     List and Search answer with the list shape, which is a different type. A generated type
    ///     nobody names is one that only has to be maintained.
    /// </remarks>
    [Fact]
    public void WhenNothingAnswersWithIt_ItIsNotWritten()
    {
        var result = RunGenerator(Source("ResourceCapabilities.List"));

        GeneratorTestHelper.GetGeneratedSource(result, "Order.ReadDto").Should().BeNull();
    }

    /// <summary>
    ///     A hard delete answers 204 and has nothing to describe, so it asks for no shape either.
    /// </summary>
    [Fact]
    public void AHardDelete_AsksForNoShape()
    {
        var result = RunGenerator("""
            using System;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Resource("orders", Capabilities = ResourceCapabilities.Delete)]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratedSource(result, "Order.ReadDto").Should().BeNull();
    }

    /// <summary>
    ///     Asking for Restore where a delete leaves no row says so, instead of generating nothing.
    /// </summary>
    /// <remarks>
    ///     The skip itself is right. What was missing is the sentence: the author asked for an endpoint,
    ///     got none, and no diagnostic explained why — the shape that produced PRAG0439 on the children
    ///     side, and PRAG0418 before that.
    /// </remarks>
    [Fact]
    public void RestoreOnAHardDeleteEntity_IsReported()
    {
        var result = RunGenerator("""
            using System;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Resource("orders", Capabilities = ResourceCapabilities.Read | ResourceCapabilities.Restore)]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2610").Should().BeTrue(
            "Restore cannot apply to an entity whose delete removes the row");
    }

    /// <summary>
    ///     And <c>All</c> is not an ask for Restore — it means "everything that applies".
    /// </summary>
    /// <remarks>
    ///     Guest in the Showcase is exactly this: <c>All</c> on an entity that is not soft-delete, six
    ///     capabilities generated out of seven, and nothing wrong. Reporting there would make the
    ///     diagnostic fire on the correct declaration, which is how a warning gets suppressed for good.
    /// </remarks>
    [Fact]
    public void AllOnAHardDeleteEntity_IsNotReported()
    {
        var result = RunGenerator("""
            using System;
            using Pragmatic.Persistence.Entity;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Resource("orders", Capabilities = ResourceCapabilities.All)]
            public partial class Order : IEntity
            {
                public string Reference { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG2610").Should().BeFalse();
    }

    /// <summary>
    ///     Whether any generated file mentions the type at all.
    /// </summary>
    /// <remarks>
    ///     Paired with the file's existence, this is the whole defect in two assertions: something
    ///     names it, and nothing writes it. A compile check would say the same thing, but this fixture
    ///     does not carry the reference closure of everything <c>[Resource]</c> emits — endpoints,
    ///     policy registries, ASP.NET — so it would report that instead.
    /// </remarks>
    private static bool SomethingNames(SourceGenRunResult result, string typeName)
    {
        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Any(file => file.Value.Contains(typeName, StringComparison.Ordinal));
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
                GeneratorTestHelper.FromType<ResourceAttribute>(),
                GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
                GeneratorTestHelper.FromTypeAssembly(typeof(Ensure.Ensure)),
                GeneratorTestHelper.FromType<Specification.Specification<object>>(),
                GeneratorTestHelper.FromType<Microsoft.EntityFrameworkCore.DbContext>(),
            ]);
    }
}
