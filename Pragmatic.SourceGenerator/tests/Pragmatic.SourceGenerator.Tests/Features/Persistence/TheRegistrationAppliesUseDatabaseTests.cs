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
///     The generated DbContext registration applies what an application passed to
///     <c>AddBoundary&lt;T&gt;(cfg =&gt; cfg.UseDatabase(…))</c> — for a boundary that can be configured, and
///     only for one.
/// </summary>
/// <remarks>
///     <para>
///         <c>BoundaryConfiguration&lt;TBoundary&gt;</c> is constrained to <c>IBoundary</c>. The line that
///         reads it is emitted for a boundary that is one — the Actions feature marks every
///         <c>[Boundary]</c> — and never for a plain class an entity names with <c>[BelongsTo&lt;T&gt;]</c>,
///         where it is CS0311. Found by the inventory corpus the first time the line was emitted for every
///         boundary.
///     </para>
///     <para>
///         Two ways a boundary reaches the host, and the answer is read differently for each: from a
///         referenced module's metadata, where the marker is already compiled in, and from the host's own
///         compilation, where it is not yet — a generator cannot see its own output.
///     </para>
/// </remarks>
public class TheRegistrationAppliesUseDatabaseTests
{
    private const string UseDatabaseLine = "global::Pragmatic.Actions.Boundary.BoundaryConfiguration<";

    // What the SDK gives every consumer through ImplicitUsings: a generated file is its own tree, so only
    // a global using reaches it.
    private const string Usings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Logging;

        """;

    private const string Module = Usings + """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Catalog;

        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [BelongsTo<CatalogBoundary>]
        public partial class Product : IEntity
        {
            public string Name { get; private set; } = "";
        }
        """;

    private const string EmptyHost = Usings + """
        namespace AppHost;

        public static class Program
        {
            public static void Main() { }
        }
        """;

    /// <summary>A host declaring its own entities: one boundary it marks, one plain class it cannot.</summary>
    private const string HostWithItsOwnBoundaries = Usings + """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;

        namespace Shop;

        public static class Program
        {
            public static void Main() { }
        }

        [Boundary]
        public partial class ShopBoundary;

        public sealed class LedgerBoundary;

        [Entity]
        [BelongsTo<ShopBoundary>]
        public partial class Order : IEntity
        {
            public string Code { get; private set; } = "";
        }

        [Entity]
        [BelongsTo<LedgerBoundary>]
        public partial class Entry : IEntity
        {
            public string Code { get; private set; } = "";
        }
        """;

    [Fact]
    public void AReferencedModulesBoundary_HasItsUseDatabaseApplied()
    {
        var module = GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Catalog.Module", Module, References);
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            EmptyHost, [.. References, module]);

        Registration(result).Should().Contain($"{UseDatabaseLine}global::Catalog.CatalogBoundary>",
            "the module compiled CatalogBoundary as an IBoundary, and the host reads that from its metadata");
        Refusals(result).Should().BeEmpty();
    }

    [Fact]
    public void TheHostsOwnBoundary_HasItsUseDatabaseApplied()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostWithItsOwnBoundaries, References);

        Registration(result).Should().Contain($"{UseDatabaseLine}global::Shop.ShopBoundary>",
            "a [Boundary] partial is marked in this same compilation, so the line compiles");
        Refusals(result).Should().BeEmpty();
    }

    /// <summary>The control: a plain class gets no line, and the registration still compiles.</summary>
    [Fact]
    public void APlainBoundaryClass_GetsNoUseDatabaseLine()
    {
        var result = GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>(
            HostWithItsOwnBoundaries, References);

        Registration(result).Should().NotContain($"{UseDatabaseLine}global::Shop.LedgerBoundary>",
            "LedgerBoundary is no IBoundary, so no application could have configured it");
        Refusals(result).Should().BeEmpty();
    }

    private static string Registration(SourceGenRunResult result)
    {
        var registration = GeneratorTestHelper.GetGeneratedSource(result, "_Infra.Persistence.DbContextRegistration");
        registration.Should().NotBeNull("the host has to emit the registration, or the assertions are about nothing");
        return registration!;
    }

    private static IEnumerable<string> Refusals(SourceGenRunResult result)
        => GeneratorTestHelper.GetCompilationErrors(result).Select(d => d.ToString());

    // The inventory corpus's set (GeneratedSurfaceInventoryTests.References), which is what a module and a
    // host with entities compile against: FromType adds one assembly and not its dependencies.
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.MultiTenancy.ITenantEntity>(),
        GeneratorTestHelper.FromType<global::System.ComponentModel.TypeConverterAttribute>(),
        .. ByName(
            "System.Text.Json",
            "System.Linq.Queryable",
            "System.ComponentModel.Annotations",
            "System.Diagnostics.DiagnosticSource",
            "Microsoft.Extensions.DependencyInjection.Abstractions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.Hosting.Abstractions",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Abstractions",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.Extensions.Configuration.Abstractions",
            "System.Private.Uri",
            "System.Security.Claims",
            "System.ComponentModel.TypeConverter",
            "Pragmatic.Result",
            "Pragmatic.Ensure",
            "Pragmatic.Specification",
            "Pragmatic.Mapping",
            "Pragmatic.Mapping.EFCore",
            "Pragmatic.Validation",
            "Pragmatic.Caching",
            "Pragmatic.Events",
            "Pragmatic.Authorization",
            "Pragmatic.Actions"),
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException(
                $"'{n}' does not resolve in the test process. Add the project reference, or the "
                + "compilation silently loses whatever needs it."));
}
