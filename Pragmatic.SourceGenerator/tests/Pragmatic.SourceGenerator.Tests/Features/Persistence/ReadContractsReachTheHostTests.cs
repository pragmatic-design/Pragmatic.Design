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
///     A module publishes a read contract; the host has to bind it.
/// </summary>
/// <remarks>
///     <para>
///         <c>[Published]</c> produced the <c>I{Module}Reads</c> interface, an implementation, and
///         <c>Add{Module}Reads()</c> — and, alone among this generator's registrations, no
///         <c>_Metadata.*</c> entry beside them. So the host had nothing to read and called nothing:
///         an action injecting the contract failed at resolution, on the <b>first request</b> rather
///         than at startup, and the one application that would have noticed had written the line by
///         hand.
///     </para>
///     <para>
///         It is the same shape as the lookup caches, one floor down — and the same one the roll-up
///         rules had. Two assemblies, because a single compilation cannot show it: the host learns
///         what to call from the metadata a <b>referenced</b> module publishes.
///     </para>
/// </remarks>
public class ReadContractsReachTheHostTests
{
    private const string ModuleWithAPublishedQuery = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class CatalogItem : IEntity
        {
            public string Sku { get; private set; } = "";
        }

        [Query<CatalogItem>]
        [Published(ContractName = "ICatalogReads")]
        public partial class GetCatalogItemBySkuQuery
        {
            public string Sku { get; init; } = "";
        }
        """;

    /// <summary>
    ///     A host, which is what the marker type makes it: the wiring is emitted only for a
    ///     compilation that references the Composition host, and this test project does not.
    /// </summary>
    private const string Host = """
        namespace Pragmatic.Composition.Hosting
        {
            public class PragmaticBuilder { }
        }

        namespace AppHost
        {
            public static class Program
            {
                public static void Main() { }
            }
        }
        """;

    /// <summary>The module publishes something the host can find.</summary>
    /// <remarks>
    ///     The first link. Without it, "the host does not bind it" cannot be told apart from "the
    ///     module never offered anything to bind", and the two have different fixes.
    /// </remarks>
    [Fact]
    public void TheModule_PublishesItsReadContract()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            ModuleWithAPublishedQuery, References);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "ReadContracts");

        metadata.Should().NotBeNull("the module publishes a contract");
        metadata!.Should().Contain("MetadataCategory.ReadContracts");
        metadata.Should().Contain("AddCatalogReads",
            "the host binds a contract by calling the registration the module named");
    }

    /// <summary>And the host binds it.</summary>
    [Fact]
    public void TheHost_BindsTheModulesReadContract()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", ModuleWithAPublishedQuery, References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, module]);

        var services = GeneratorTestHelper.GetGeneratedSource(result, "Host.Services");

        services.Should().NotBeNull("the host wires the services it discovered");
        services!.Should().Contain("AddCatalogReads",
            "an action injecting ICatalogReads resolves at startup or fails at the first request");
    }

    /// <summary>
    ///     The control: a module that publishes nothing makes the host say nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the host binds it" is satisfied by a host that emits the call unconditionally,
    ///     which would name a registration nobody generated.
    /// </remarks>
    [Fact]
    public void AHostWithNoPublishedQuery_BindsNothing()
    {
        // The attribute alone is removed, not its line: the raw string carries the line endings of the
        // checkout, CRLF on Windows, where a pattern ending in "\n" removed nothing.
        var unpublished = ModuleWithAPublishedQuery.Replace("[Published(ContractName = \"ICatalogReads\")]", "");
        unpublished.Should().NotContain("[Published");

        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module",
            unpublished,
            References);

        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            Host, [..References, module]);

        var services = GeneratorTestHelper.GetGeneratedSource(result, "Host.Services");

        services.Should().NotBeNull("the host is still a host");
        services!.Should().NotContain("Reads(services)");
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.ReadAccessAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        .. ByName(
            "System.Text.Json",
            "System.ComponentModel.TypeConverter",
            "System.ComponentModel.Annotations",
            "System.Linq.Queryable",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
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
