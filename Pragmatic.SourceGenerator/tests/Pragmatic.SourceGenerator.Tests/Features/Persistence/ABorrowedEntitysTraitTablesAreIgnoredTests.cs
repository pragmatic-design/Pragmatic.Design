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
///     A boundary that borrows an entity to read it does not inherit that entity's trait tables.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>CollectCrossBoundaryTypes</c> walks <c>EntityMetadataModel.Navigations</c>, which holds
///         the navigations an entity <b>declares</b>. A trait's navigation is generated —
///         <c>ICollection&lt;{Parent}Tag&gt; Tags</c> — so the junction is invisible to that walk, while
///         EF Core discovers it by convention the moment the parent is in the model. The borrowing
///         context then held a junction with neither configuration nor key.
///     </para>
///     <para>
///         What that looked like: <c>The entity type 'ProductTagLink' requires a primary key to be
///         defined</c> — a 500 on <b>every</b> request served by the borrowing boundary, caused by a
///         trait declared in another one. Ninety-three integration tests of a consumer application went
///         red, in areas that have nothing to do with tags.
///     </para>
///     <para>
///         Ignored rather than configured: a context that borrows an entity in order to read it has no
///         business writing that entity's tags.
///     </para>
///     <para>
///         Three stages, as <c>ReadAccessAcrossAssembliesTests</c> next door explains at length: the
///         reading context is built from module metadata the generator itself emits, so a
///         one-compilation case produces no reading context at all and a test written that way asserts
///         nothing.
///     </para>
/// </remarks>
public class ABorrowedEntitysTraitTablesAreIgnoredTests
{
    /// <summary>The owner: an entity that carries tags.</summary>
    private const string OwnerModule = """
        // ⚠️ global, not a plain using: the trait templates emit a bare `Guid`, which a real project
        // resolves through implicit usings and this harness does not. It applies to the generated
        // files of this compilation, which is the only place it can be added.
        global using System;
        global using System.Collections.Generic;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.Linq;

        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Tags;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        [HasTags]
        public partial class Product : IEntity
        {
            public string Name { get; private set; } = "";
        }
        """;

    /// <summary>The reader: it borrows the entity, and wants nothing to do with its tags.</summary>
    private const string ReadingModule = """
        using Catalog;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Work;

        [Boundary]
        [ReadAccess<Product>]
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

    private static string Context(string boundary)
    {
        var owner = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", OwnerModule, References);

        var reader = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Work.Module", ReadingModule, [.. References, owner]);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [.. References, owner, reader]);

        var context = GeneratorTestHelper.GetGeneratedSource(result, $"DbContext.{boundary}");

        context.Should().NotBeNull(
            $"the {boundary} context has to be generated, or the assertions below are about nothing");

        return context!;
    }

    /// <summary>
    ///     The borrowing context ignores the junction it would otherwise discover.
    /// </summary>
    [Fact]
    public void TheBorrowingContext_IgnoresTheTagJunctionOfTheEntityItReads()
    {
        Context("Work").Should().Contain("Ignore<Catalog.ProductTagLink>",
            "EF finds it through the generated navigation on the entity this context borrows, and "
            + "nothing here configures it");
    }

    /// <summary>
    ///     The control: the owning context still configures it.
    /// </summary>
    /// <remarks>
    ///     Without this half, "the junction is ignored" is satisfied by a fix that ignores it
    ///     everywhere — which would leave the trait with no table at all and every tag write failing in
    ///     the boundary that declared the trait.
    /// </remarks>
    [Fact]
    public void TheOwningContext_StillConfiguresIt()
    {
        var catalog = Context("Catalog");

        catalog.Should().Contain("ApplyConfiguration(new ProductTagLinkEntityConfig())",
            "the rows belong here, and this is the context that writes them");
        catalog.Should().NotContain("Ignore<Catalog.ProductTagLink>",
            "ignoring it here would leave the trait without a table");
    }

    /// <summary>Everything the two modules and the host are compiled against.</summary>
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Tags.HasTagsAttribute>(),
        .. ByName(
            "System.Runtime",
            "System.Data.Common",
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
            "Pragmatic.Actions",
            "Pragmatic.Authorization",
            "Pragmatic.Tags"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
