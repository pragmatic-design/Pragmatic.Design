using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An entity's owned identity survives its relations.
/// </summary>
/// <remarks>
///     The owned type (anything deriving from <c>Pragmatic.Identity.IdentityRecord</c>, like
///     <c>LocalIdentity</c>) was collected only for entities that declared no relation. A user entity
///     that also belonged to a team got <c>builder.Ignore(e => e.Identity)</c>: no credentials columns,
///     and a translation error on the first sign-in. The Showcase's user entity declares no relation,
///     which is why nothing ever saw it.
/// </remarks>
public class AnOwnedIdentityNextToARelationTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Composition.Attributes.ModuleAttribute>()
    ];

    private const string MetadataFile = "_Metadata.Persistence.g.cs";

    private static string PersistenceMetadata(string relation)
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Pragmatic.Identity
            {
                public abstract class IdentityRecord { public string ExternalIdentityKey { get; set; } = ""; }
            }

            namespace Contoso.Staff
            {
                [Boundary]
                public partial class StaffBoundary;

                public sealed class Credentials : Pragmatic.Identity.IdentityRecord
                {
                    public string PasswordHash { get; set; } = "";
                }
            }

            namespace Contoso.Staff.Entities
            {
                [Entity]
                public partial class Department : IEntity
                {
                    public string Name { get; private set; } = "";
                }

                [Entity]
                {{relation}}
                public partial class Person : IEntity
                {
                    public string FullName { get; private set; } = "";

                    public Credentials? Identity { get; set; }
                }
            }
            """, References);

        var files = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        files.Should().ContainKey(MetadataFile);
        return files[MetadataFile];
    }

    [Fact]
    public void WithARelation_TheIdentityIsStillAnOwnedNavigation()
    {
        var metadata = PersistenceMetadata("[Relation.ManyToOne<Department>.WithNavigation(\"Department\", Required = false)]");

        metadata.Should().Contain("\"navigationType\": \"Owned\"",
            "the credentials are part of the person's row; ignoring them leaves the entity with none");
    }

    /// <summary>The control: the shape that always worked.</summary>
    [Fact]
    public void WithoutARelation_TheIdentityIsAnOwnedNavigation()
    {
        var metadata = PersistenceMetadata("");

        metadata.Should().Contain("\"navigationType\": \"Owned\"");
    }
}
