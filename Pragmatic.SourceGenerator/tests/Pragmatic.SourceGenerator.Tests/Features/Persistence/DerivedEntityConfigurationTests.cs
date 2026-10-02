using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Composition.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A derived <c>[Entity]</c> in a TPH hierarchy configures its own properties and nothing about the
///     table, the key, the filters or the indexes — those belong to the root.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It configured all of them. <c>RenderConfigure</c> emitted <c>ToTable</c> and
///         <c>HasKey</c> unconditionally, and the derived type inherits every trait of its base, so the
///         soft-delete query filter and the trait indexes came out a second time as well. The build was
///         clean and the DbContext died at first use — "A key cannot be configured on 'X' because it is
///         a derived type" — with every request through that boundary answering 500. Measured on a
///         consumer application whose domain needed the derived types to have repositories, mutations
///         and permissions of their own, which is exactly what <c>[Entity]</c> on them buys and what the
///         attribute's own documentation shows.
///     </para>
///     <para>
///         The base-type field this needs was already on the model and already read three lines below
///         the offending <c>HasKey</c>, for a different purpose.
///     </para>
/// </remarks>
public class DerivedEntityConfigurationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<ModuleAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run() =>
        GeneratorTestHelper.RunGeneratorAsHost<PragmaticSourceGenerator>("""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Composition.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Billing;

            [Boundary]
            public partial class BillingBoundary;

            [Entity]
            [SoftDelete]
            [Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "FeeType")]
            public partial class Fee : IEntity
            {
                public decimal Amount { get; private set; }
            }

            [Entity]
            public partial class ServiceFee : Fee
            {
                public string ServiceName { get; private set; } = "";
            }

            public static class Program { public static void Main() { } }
            """, References);

    private static string ConfigOf(SourceGenRunResult result, string entity)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var hit = sources.FirstOrDefault(kv => kv.Key.Contains($"EntityConfig.") && kv.Key.Contains($".{entity}."));
        return hit.Value ?? "";
    }

    [Fact]
    public void TheDerivedConfiguration_ClaimsNeitherTableNorKey()
    {
        var result = Run();

        var root = ConfigOf(result, "Fee");
        root.Should().Contain("builder.ToTable(\"Fees\")", "the control: the root owns the table");
        root.Should().Contain("builder.HasKey(e => e.PersistenceId)", "and the key");

        var derived = ConfigOf(result, "ServiceFee");
        derived.Should().NotBeEmpty("a derived [Entity] still gets a configuration of its own");
        derived.Should().NotContain("ToTable",
            "the derived type shares the root's table; naming one of its own emits three tables where "
            + "TPH wants one");
        derived.Should().NotContain("HasKey",
            "EF refuses a key on a derived type, and refuses it at first use rather than at build");
    }

    [Fact]
    public void TheDerivedConfiguration_DoesNotRepeatTheRootsFiltersAndIndexes()
    {
        var result = Run();

        var root = ConfigOf(result, "Fee");
        root.Should().Contain("builder.HasQueryFilter(\"SoftDelete\"",
            "the control: the root declares [SoftDelete] and carries its filter");

        var derived = ConfigOf(result, "ServiceFee");
        derived.Should().NotContain("HasQueryFilter",
            "the filter is the hierarchy's, declared once on the root");
        derived.Should().NotContain("HasIndex",
            "and so are its indexes — they are on columns of the shared table");
    }

    [Fact]
    public void TheDerivedConfiguration_StillConfiguresItsOwnProperties()
    {
        var derived = ConfigOf(Run(), "ServiceFee");

        derived.Should().Contain("ServiceName",
            "its own columns are the part that is genuinely its own to configure");
    }

    // The other half of this defect — the migration schema counting a TPH-derived type as a table of
    // its own, on top of merging its columns into the root's — has no signal at this level: this
    // harness emits no _Infra.Persistence.SchemaMetadata without a [Database] declaration, and a
    // hand-built EntityMetadataModel would prove only that the assertion matches the model I wrote.
    // It is measured against the real database instead, in Showcase.IntegrationTests
    // InheritanceTests.TheHierarchy_IsOneTableInTheMigratedSchema.
}
