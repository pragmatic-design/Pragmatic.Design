using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[GenerateTimeline]</c> emits the LAG/LEAD query for a temporal relation.
/// </summary>
public class ATimelineIsGeneratedForATemporalRelationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.ModuleAttribute>()
    ];

    [Fact]
    public void ATemporalRelationThatAsksForIt_GetsAGetTimeline()
    {
        var files = Generated("[GenerateTimeline]");

        files.Keys.Should().Contain(k => k.Contains("TimelineQuery"));
    }

    /// <summary>The control: the query is opt-in, so an entity that does not ask gets none.</summary>
    [Fact]
    public void ATemporalRelationThatDoesNot_GetsNone()
        => Generated(declaration: "").Keys.Should().NotContain(k => k.Contains("TimelineQuery"));

    private static Dictionary<string, string> Generated(string declaration)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Staff
            {
                [Boundary]
                public partial class StaffBoundary;
            }

            namespace Contoso.Staff.Entities
            {
                [Entity]
                public partial class Site : IEntity
                {
                    public string Name { get; private set; } = "";
                }

                [Entity]
                [Relation.ManyToOne<Site>]
                [TemporalRelation<Site>(MaxActive = 1)]
                {{declaration}}
                public partial class Assignment : IEntity, ITemporalRelation
                {
                    public Guid StaffId { get; private set; }

                    public DateTimeOffset ValidFrom { get; set; }
                    public DateTimeOffset? ValidTo { get; set; }
                }
            }
            """, References);

        return GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
    }
}
