using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     BuildInheritanceConfigs references a {Type}InheritanceConfiguration only for the hierarchy ROOT
///     (no entity base type), because only the root generates a config. A derived leaf carrying
///     [Inheritance(DiscriminatorValue=)] also has a non-empty InheritanceStrategy but no config of its
///     own, so referencing it would produce a CS0103 in the BoundaryDbContext.
/// </summary>
public class InheritanceConfigFilterTests
{
    [Fact]
    public void BuildInheritanceConfigs_IncludesRoot_ExcludesDerivedLeaf()
    {
        var root = Model("Fee", baseEntity: null);
        var derived = Model("ServiceFee", baseEntity: "MyApp.Billing.Fee");

        var configs = DbContextFeature.BuildInheritanceConfigs([root, derived]);

        configs.Should().ContainSingle();
        configs[0].Should().Contain("FeeInheritanceConfiguration");
        configs.Should().NotContain(c => c.Contains("ServiceFeeInheritanceConfiguration"));
    }

    [Fact]
    public void BuildInheritanceConfigs_NoInheritance_ReturnsEmpty()
    {
        var plain = Model("Widget", baseEntity: null, strategy: null);

        DbContextFeature.BuildInheritanceConfigs([plain]).Should().BeEmpty();
    }

    private static EntityMetadataModel Model(string name, string? baseEntity, string? strategy = "TPH") => new()
    {
        TypeName = name,
        FullTypeName = $"MyApp.Billing.{name}",
        Namespace = "MyApp.Billing",
        IdType = "Guid",
        InheritanceStrategy = strategy,
        BaseEntityFullTypeName = baseEntity,
        Properties = ImmutableArray<PropertyMetadataModel>.Empty,
        Navigations = ImmutableArray<NavigationMetadataModel>.Empty,
        RelationAttributes = ImmutableArray<RelationAttributeModel>.Empty,
        AllSourceMemberNames = ImmutableArray<string>.Empty,
        GeneratedRelationProperties = ImmutableArray<GeneratedRelationPropertyModel>.Empty
    };
}
