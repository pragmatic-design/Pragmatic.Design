using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Templates;

/// <summary>
///     The timeline template defers to the provider-aware TimelineCteSql, with table/column names
///     resolved from the EF model and a partition by parent — no SQL Server bracket delimiters and no
///     naively pluralized table name.
/// </summary>
public class TimelineQueryTemplateTests
{
    [Fact]
    public void GetTimeline_DelegatesToProviderAwareTimelineCteSql_NoHardCodedDialect()
    {
        var source = Render(parentFk: "PropertyId");

        // The provider-aware runtime helper builds the SQL — not a hard-coded string.
        source.Should().Contain("global::Pragmatic.Persistence.EFCore.Query.TimelineCteSql.Build(");
        // No SQL-Server-only bracket-delimited table literal in the generated code.
        source.Should().NotContain("FROM [");
        source.Should().NotContain("[StaffAssignments]");
    }

    [Fact]
    public void GetTimeline_ResolvesTableAndColumnsFromEfModel_NotNaivePlural()
    {
        var source = Render(parentFk: "PropertyId");

        // Table/column names come from the mapped model, honouring [Table]/[Column]/irregular plurals.
        source.Should().Contain("dbContext.Model.FindEntityType(");
        source.Should().Contain(".GetTableName()");
        source.Should().Contain("FindProperty(\"ValidFrom\")?.GetColumnName()");
    }

    [Fact]
    public void GetTimeline_WithParentFk_PartitionsByParentColumn()
    {
        var source = Render(parentFk: "PropertyId");

        // The partition column is resolved from the parent FK so LAG/LEAD do not cross parents.
        source.Should().Contain("FindProperty(\"PropertyId\")?.GetColumnName()");
    }

    [Fact]
    public void GetTimeline_WithoutParentFk_PassesNullPartition()
    {
        var source = Render(parentFk: null);

        source.Should().Contain("string? __partition = null;");
    }

    private static string Render(string? parentFk)
    {
        var model = new EntityMetadataModel
        {
            TypeName = "StaffAssignment",
            FullTypeName = "MyApp.Entities.StaffAssignment",
            Namespace = "MyApp.Entities",
            IdType = "Guid",
            IsTemporalRelation = true,
            TemporalParentFkProperty = parentFk,
            Properties = ImmutableArray<PropertyMetadataModel>.Empty,
            Navigations = ImmutableArray<NavigationMetadataModel>.Empty,
            RelationAttributes = ImmutableArray<RelationAttributeModel>.Empty,
            AllSourceMemberNames = ImmutableArray<string>.Empty,
            GeneratedRelationProperties = ImmutableArray<GeneratedRelationPropertyModel>.Empty
        };

        return new TimelineQueryTemplate(model).RenderOutput().Text;
    }
}
