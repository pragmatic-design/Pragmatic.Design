using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[ReadAccess&lt;T&gt;]</c> across assemblies: one module owns the entity, another declares the
///     read, and the host builds the reading context from what both published.
/// </summary>
/// <remarks>
///     <para>
///         There was no test for this, and there could not be one in a single compilation. The reading
///         context is built from <c>PragmaticModuleMetadataAttribute</c> — which the generator itself
///         emits — so a one-compilation case produces neither the <c>DbSet</c> nor the
///         <c>ExcludeFromMigrations</c> line, and a test written that way asserts nothing at all. That
///         is why <c>ReadAccessEntities</c> was carried, rendered, and never pinned.
///     </para>
///     <para>
///         Three stages, each generated, as an application has them: the owner module, the reading
///         module, then the host that references both.
///     </para>
/// </remarks>
public class ReadAccessAcrossAssembliesTests
{
    /// <summary>The owner: an entity whose many-to-many points back at itself.</summary>
    private const string OwnerModule = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        [Relation.ManyToMany<KnowledgeItem>.WithNavigation("SeeAlso", Inverse = "SeenFrom",
            JoinTable = "KnowledgeItemSeeAlso")]
        [Relation.ManyToMany<KnowledgeItem>.WithNavigation("SeenFrom", Inverse = "SeeAlso",
            JoinTable = "KnowledgeItemSeeAlso")]
        public partial class KnowledgeItem : IEntity
        {
            public string Term { get; private set; } = "";
        }
        """;

    /// <summary>The reader: its own entity, and the declaration that crosses the line.</summary>
    private const string ReadingModule = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<KnowledgeItem>]
        public partial class WorkBoundary;

        [Entity]
        [BelongsTo<WorkBoundary>]
        public partial class Story : IEntity
        {
            public string Title { get; private set; } = "";
        }
        """;

    private const string Host = """
        namespace AppHost;

        public static class Program
        {
            public static void Main() { }
        }
        """;

    private static string ReadingDbContext()
    {
        var owner = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", OwnerModule, References);

        var reader = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Work.Module", ReadingModule, [..References, owner]);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, owner, reader]);

        var context = GeneratorTestHelper.GetGeneratedSource(result, "DbContext.Work");

        context.Should().NotBeNull(
            "the host references a module whose boundary declares [ReadAccess], so its context is "
            + "generated — and if this is null the assertions below are about nothing");

        return context!;
    }

    /// <summary>
    ///     Stage two on its own: the reading module publishes its declaration for the host to read.
    /// </summary>
    /// <remarks>
    ///     The chain has two links and this is the first. Without it, "the context has no DbSet" cannot
    ///     be told apart from "the module never said it wanted one", and the two have different fixes.
    /// </remarks>
    [Fact]
    public void TheReadingModule_PublishesItsReadAccessDeclaration()
    {
        var owner = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", OwnerModule, References);

        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            ReadingModule, [..References, owner]);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "ModuleMetadata");

        metadata.Should().NotBeNull("the boundary is what the module metadata is emitted for");
        metadata!.Should().Contain("ReadAccessTypes",
            "the declaration has to leave the module, or the host cannot act on it");
        metadata.Should().Contain("Catalog.KnowledgeItem");
    }

    /// <summary>
    ///     The declaration reaches the reading context: a <c>DbSet</c>, excluded from migrations.
    /// </summary>
    /// <remarks>
    ///     The table belongs to the owner, so the reader must not create it. This is the half of
    ///     <c>[ReadAccess]</c> the specification states and nothing in this repository measured.
    /// </remarks>
    [Fact]
    public void ReadAccess_AddsADbSetExcludedFromMigrations()
    {
        var context = ReadingDbContext();

        context.Should().Contain("DbSet<Catalog.KnowledgeItem>",
            "the read entity gets a set in the reading context");
        context.Should().Contain(
            "modelBuilder.Entity<Catalog.KnowledgeItem>().ToTable(t => t.ExcludeFromMigrations())",
            "the owner creates the table, not the reader");
    }

    /// <summary>
    ///     A self-referential many-to-many on the read entity does not leave a skip navigation dangling.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>[ReadAccess]</c> brings the owner's whole per-entity configuration, so the closure of
    ///         its graph comes too and the types the reader does not own go into <c>Ignore&lt;T&gt;()</c>.
    ///         Towards another type that closes, because ignoring the target removes the skip navigation
    ///         with it. Pointing back at itself it does not: the target is the entity being read.
    ///     </para>
    ///     <para>
    ///         The consequence was measured in a consumer, not here: «The skip navigation
    ///         'KnowledgeItem.RelatedFrom' doesn't have a foreign key», every request of the boundary
    ///         answering 500 over a clean compilation. A generator test cannot build an EF model, so
    ///         what it can hold is that the navigation is dropped by name — and the model that builds is
    ///         asserted in the consumer.
    ///     </para>
    /// </remarks>
    [Fact]
    public void SelfReferentialSkipNavigations_AreDroppedFromTheReadingContext()
    {
        var context = ReadingDbContext();

        context.Should().Contain("Ignore(\"SeeAlso\")");
        context.Should().Contain("Ignore(\"SeenFrom\")");
    }

    /// <summary>
    ///     Everything the two modules and the host are compiled against.
    /// </summary>
    /// <remarks>
    ///     Explicit, and a name that does not resolve throws: a dropped reference here does not fail
    ///     loudly — the attribute stops binding and the feature is silently not generated, which reads
    ///     exactly like a clean run.
    /// </remarks>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
