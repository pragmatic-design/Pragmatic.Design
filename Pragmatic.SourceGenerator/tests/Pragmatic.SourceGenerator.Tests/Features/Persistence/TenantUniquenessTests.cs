using System.Collections.Immutable;
using System.Threading;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     On a tenant-scoped entity, a declared uniqueness is unique <em>within the tenant</em>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It was unique across all of them. <c>ITenantEntity</c> buys visibility — the filter hides
///         other tenants' rows — and nothing about uniqueness, so the generated index covered the
///         marked columns alone. One workspace taking a value took it from every other: the second
///         got a conflict that named nothing, and learned from it that somebody already held that
///         value. Measured in a consumer application, where three tenant-scoped entities out of four
///         had the defect and two had already been worked around by hand, independently — which is
///         what a missing feature looks like from the outside.
///     </para>
///     <para>
///         The round-trip tests are the ones that matter. The entity configuration is generated in the
///         host from the module's assembly metadata, so a fact the metadata document does not carry
///         reads back as a fact the entity does not have — and the schema comes out per-tenant no
///         matter what the attribute said.
///     </para>
/// </remarks>
public class TenantUniquenessTests
{
    private static EntityMetadataModel Model(
        bool isTenantEntity = true,
        bool keyIsGlobal = false,
        params UniqueIndexModel[] uniqueIndexes) => new()
    {
        TypeName = "Grant",
        FullTypeName = "Workspaces.Grant",
        Namespace = "Workspaces",
        IdType = "Guid",
        IsValid = true,
        IsTenantEntity = isTenantEntity,
        LogicKeys = ImmutableArray.Create(
            new LogicKeyPart { Name = "Role", TypeName = "string", IsGlobal = keyIsGlobal },
            new LogicKeyPart { Name = "Permission", TypeName = "string", IsGlobal = keyIsGlobal }),
        UniqueIndexes = uniqueIndexes.ToImmutableArray()
    };

    private static UniqueIndexModel Index(bool isGlobal, params string[] columns) => new()
    {
        Columns = columns.ToImmutableArray(),
        IsGlobal = isGlobal
    };

    [Fact]
    public void OnATenantEntity_TheDomainKeyIndexLeadsWithTheTenant()
    {
        var source = new EntityConfigurationTemplate(Model()).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.TenantId, e.Role, e.Permission }).IsUnique();");
    }

    /// <summary>The control: an entity that is not tenant-scoped has no tenant to lead with.</summary>
    /// <remarks>
    ///     Without this, prepending TenantId unconditionally would also pass the test above — and
    ///     produce a configuration naming a column the entity does not have.
    /// </remarks>
    [Fact]
    public void OnAnOrdinaryEntity_TheIndexIsTheDeclaredColumnsAlone()
    {
        var source = new EntityConfigurationTemplate(Model(isTenantEntity: false)).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.Role, e.Permission }).IsUnique();")
            .And.NotContain("e.TenantId, e.Role");
    }

    /// <summary>A key that asked to be global stays global.</summary>
    [Fact]
    public void AGlobalDomainKey_IsNotScopedToTheTenant()
    {
        var source = new EntityConfigurationTemplate(Model(keyIsGlobal: true)).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.Role, e.Permission }).IsUnique();");
    }

    [Fact]
    public void ADeclaredUniqueIndex_IsScopedToTheTenantToo()
    {
        var source = new EntityConfigurationTemplate(
            Model(uniqueIndexes: Index(isGlobal: false, "Email"))).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();");
    }

    /// <summary>
    ///     An entity can declare more than one, which is the whole reason this attribute exists.
    /// </summary>
    /// <remarks>
    ///     Without it, <c>[LogicKey]</c> is the only way to declare a unique index and there is one
    ///     domain key — so a second constraint would have to be written by hand in
    ///     <c>OnModelCreating</c>, where the migrations never see it, or go unenforced.
    /// </remarks>
    [Fact]
    public void SeveralUniqueIndexes_AreAllEmitted()
    {
        var source = new EntityConfigurationTemplate(Model(
            uniqueIndexes: [Index(false, "Email"), Index(true, "ExternalCode")])).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();")
            .And.Contain("builder.HasIndex(e => e.ExternalCode).IsUnique();");
    }

    [Fact]
    public void TheKeyScope_SurvivesTheAssemblyMetadataChannel()
    {
        var written = new PersistenceMetadataTemplate([Model(keyIsGlobal: true)], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        read[0].LogicKeys.AsImmutableArray().Should().OnlyContain(p => p.IsGlobal);
    }

    /// <summary>The control for the channel: a per-tenant key reads back per-tenant.</summary>
    [Fact]
    public void APerTenantKey_SurvivesTheChannelAsPerTenant()
    {
        var written = new PersistenceMetadataTemplate([Model()], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        read[0].LogicKeys.AsImmutableArray().Should().OnlyContain(p => !p.IsGlobal);
        read[0].LogicKeyIsPerTenant.Should().BeTrue();
    }

    [Fact]
    public void UniqueIndexes_SurviveTheAssemblyMetadataChannel()
    {
        var written = new PersistenceMetadataTemplate(
            [Model(uniqueIndexes: [Index(false, "Email", "WorkspaceId"), Index(true, "ExternalCode")])],
            indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();

        var indexes = read[0].UniqueIndexes.AsImmutableArray();
        indexes.Length.Should().Be(2);

        indexes[0].Columns.AsImmutableArray().Should().Equal("Email", "WorkspaceId");
        indexes[0].IsGlobal.Should().BeFalse();

        indexes[1].Columns.AsImmutableArray().Should().Equal("ExternalCode");
        indexes[1].IsGlobal.Should().BeTrue("a scope that does not cross the channel is a schema that "
            + "disagrees with the attribute, on the far side, silently");
    }

    /// <summary>The control: an entity with no [Unique] reads back with none.</summary>
    [Fact]
    public void NoUniqueIndexes_SurviveTheChannelAsNone()
    {
        var written = new PersistenceMetadataTemplate([Model()], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        read[0].UniqueIndexes.Length.Should().Be(0);
    }
}
