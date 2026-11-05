using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[Index]</c>, the non-unique counterpart of <c>[Unique]</c> (#524): read from the entity, written into EF Core's configuration,
///     carried through the metadata a host reads a referenced module's entities from, and checked by name (<c>PRAG0627</c>). The engines'
///     schemas are compared with EF Core's in <c>TheSqlEnginesSchemaIsPragmaticsTests</c> and run in the documents suite.
/// </summary>
public class IndexAttributeTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Members;

            [Boundary]
            public partial class MembersBoundary;

            {{body}}
            """, References);

    private static EntityMetadataModel Model(bool isTenantEntity, params IndexModel[] indexes) => new()
    {
        TypeName = "Assignment",
        FullTypeName = "Access.Assignment",
        Namespace = "Access",
        IdType = "Guid",
        IsValid = true,
        IsTenantEntity = isTenantEntity,
        Indexes = indexes.ToImmutableArray()
    };

    private static IndexModel Index(params string[] columns) => new() { Columns = columns.ToImmutableArray() };

    /// <summary>The JSON document of a generated <c>[assembly: PragmaticMetadata(…, """…""")]</c>.</summary>
    private static string Json(string metadataSource)
    {
        var start = metadataSource.IndexOf("\"\"\"", StringComparison.Ordinal) + 3;
        return metadataSource.Substring(start, metadataSource.LastIndexOf("\"\"\"", StringComparison.Ordinal) - start);
    }

    /// <summary>The attribute is read: the module's metadata, which the host writes the configuration and the schema from, carries it.</summary>
    [Fact]
    public void TheEntitysIndex_ReachesTheModulesMetadata()
    {
        var result = Run("""
            [Entity]
            [Index(nameof(UserId), nameof(RoleName))]
            public partial class Assignment : IEntity
            {
                public string UserId { get; private set; } = "";
                public string RoleName { get; private set; } = "";
            }
            """);

        var metadata = GeneratorTestHelper.GetGeneratedSource(result, "_Metadata.Persistence") ?? throw new InvalidOperationException("No persistence metadata was generated.");
        var read = EntityMetadataReader.ParseEntitiesFromJson(Json(metadata), CancellationToken.None);
        read.Single(e => e.TypeName == "Assignment").Indexes.AsImmutableArray().Should().ContainSingle()
            .Which.Columns.AsImmutableArray().Should().Equal("UserId", "RoleName");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0627").Should().BeFalse("both names are the entity's properties");
    }

    [Fact]
    public void TheConfiguration_IndexesTheColumnsAsWritten_AndNothingMore()
    {
        // On a tenant entity too: an [Index] is the columns written, where a [Unique] is led by the tenant.
        var source = new EntityConfigurationTemplate(Model(isTenantEntity: true, Index("UserId", "RoleName"), Index("RoleName"))).RenderOutput().Text;

        source.Should().Contain("builder.HasIndex(e => new { e.UserId, e.RoleName });")
            .And.Contain("builder.HasIndex(e => e.RoleName);")
            .And.NotContain("e.TenantId, e.UserId");
    }

    /// <summary>The control: an entity with no [Index] gets no index from it.</summary>
    [Fact]
    public void WithNoIndex_TheConfigurationDeclaresNone()
    {
        var source = new EntityConfigurationTemplate(Model(isTenantEntity: false)).RenderOutput().Text;

        source.Should().NotContain("builder.HasIndex(");
    }

    [Fact]
    public void Indexes_SurviveTheAssemblyMetadataChannel()
    {
        var written = new PersistenceMetadataTemplate([Model(false, Index("UserId", "RoleName"), Index("GroupName"))], indent: false).BuildJson();

        var read = EntityMetadataReader.ParseEntitiesFromJson(written, CancellationToken.None);

        read.Should().ContainSingle();
        var indexes = read[0].Indexes.AsImmutableArray();
        indexes.Length.Should().Be(2, "an index that does not cross the channel is not in the host's schema");
        indexes[0].Columns.AsImmutableArray().Should().Equal("UserId", "RoleName");
        indexes[1].Columns.AsImmutableArray().Should().Equal("GroupName");
    }

    [Fact]
    public void AnIndexNamingAPropertyTheEntityDoesNotHave_IsReported()
    {
        var result = Run("""
            [Entity]
            [Index("Usr")]
            public partial class Assignment : IEntity
            {
                public string UserId { get; private set; } = "";
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0627").Select(d => d.GetMessage()).Should()
            .ContainSingle(m => m.Contains("[Index]", StringComparison.Ordinal) && m.Contains("'Usr'", StringComparison.Ordinal));
    }
}
