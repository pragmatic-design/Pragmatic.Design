using System.Collections.Immutable;
using System.Threading;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A declared <c>[VisibleWhen&lt;TRule&gt;]</c> rule becomes an EF Core named global query filter.
/// </summary>
/// <remarks>
///     <para>
///         Why the EF model rather than the Pragmatic filter provider: the provider is called at the
///         root of a query, and a projected collection never passes through it — the executor filters
///         the entity queryable and adds the projection afterwards, so the navigation visitor is
///         handed an expression with no navigation in it. EF applies a model-level filter to every
///         query touching the entity, an <c>Include</c> and a projected subquery included.
///     </para>
///     <para>
///         The round-trip test below is the one that matters. The entity configuration is generated in
///         the <b>host</b>, which reads entities from the module's assembly metadata in preference to
///         scanning its symbols — so a fact the metadata document does not carry does not reach the
///         host at all, and the configuration it generates is byte-identical to one for an entity with
///         no rule. Symbol transforms that read the attribute while the metadata writer does not write
///         it leave one visible symptom: rows that are meant to be hidden and are not.
///     </para>
/// </remarks>
public class VisibilityRuleConfigTests
{
    private static EntityMetadataModel Model(params string[] rules) => new()
    {
        TypeName = "Property",
        FullTypeName = "Catalog.Property",
        Namespace = "Catalog",
        IdType = "Guid",
        IsValid = true,
        VisibilityRules = rules.ToImmutableArray()
    };

    [Fact]
    public void DeclaredRule_EmitsANamedQueryFilter()
    {
        var source = new EntityConfigurationTemplate(Model("global::Catalog.ActiveOnly"))
            .RenderOutput().Text;

        source.Should().Contain(
            "builder.HasQueryFilter(\"Visibility:Catalog.ActiveOnly\", new global::Catalog.ActiveOnly().ToExpression());");
    }

    /// <summary>
    ///     The control: without a rule there is no filter, and no empty section either.
    /// </summary>
    [Fact]
    public void NoRule_EmitsNoVisibilityFilter()
    {
        var source = new EntityConfigurationTemplate(Model()).RenderOutput().Text;

        source.Should().NotContain("Visibility:");
    }

    /// <summary>
    ///     Two rules AND-combine, which is what EF Core does with two named filters.
    /// </summary>
    [Fact]
    public void TwoRules_EmitTwoNamedFilters()
    {
        var source = new EntityConfigurationTemplate(
            Model("global::Catalog.ActiveOnly", "global::Catalog.PublishedOnly")).RenderOutput().Text;

        source.Should().Contain("\"Visibility:Catalog.ActiveOnly\"")
            .And.Contain("\"Visibility:Catalog.PublishedOnly\"");
    }

    /// <summary>
    ///     The rule survives the channel the host actually reads.
    /// </summary>
    /// <remarks>
    ///     Both ends in one test on purpose. Testing the writer against a literal and the reader
    ///     against another literal would have passed on the broken pair: each end was self-consistent,
    ///     and the key simply was not in either.
    /// </remarks>
    [Fact]
    public void DeclaredRule_SurvivesTheAssemblyMetadataChannel()
    {
        var written = new PersistenceMetadataTemplate(
            [Model("global::Catalog.ActiveOnly")], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        read[0].VisibilityRules.AsImmutableArray().Should().ContainSingle()
            .Which.Should().Be("global::Catalog.ActiveOnly");
    }

    /// <summary>
    ///     The control for the channel: an entity with no rule reads back with none.
    /// </summary>
    [Fact]
    public void NoRule_SurvivesTheChannelAsNone()
    {
        var written = new PersistenceMetadataTemplate([Model()], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        read[0].VisibilityRules.Length.Should().Be(0);
    }
}
