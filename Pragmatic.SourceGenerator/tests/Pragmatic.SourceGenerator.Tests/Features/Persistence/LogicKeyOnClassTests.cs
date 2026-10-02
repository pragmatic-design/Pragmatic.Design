using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[LogicKey("A", "B")]</c> on the class: the one form that can include a generated foreign key.
/// </summary>
/// <remarks>
///     <para>
///         A relation's key is a generated member, and an attribute cannot sit on a member the class
///         does not have. Without this form the only way to put a foreign key into a domain key is to
///         write the key by hand — the form <c>PRAG0619</c> forbids. The case it serves: a membership
///         identified by (workspace, external id), where the workspace id is the key the parent's
///         <c>[Relation.OneToMany]</c> generates.
///     </para>
///     <para>
///         The part's type comes from the relation graph, not from a second prediction of the naming:
///         the assertion on <c>Guid workspaceId</c> is the assertion that the graph was consulted.
///     </para>
/// </remarks>
public class LogicKeyOnClassTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string memberAttributes, string memberBody = "")
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using System;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Tenancy;

            [Boundary]
            public partial class TenancyBoundary;

            [Entity]
            [Relation.OneToMany<Member>]
            public partial class Workspace : IEntity { public string Name { get; private set; } = ""; }

            [Entity]
            {{memberAttributes}}
            public partial class Member : IEntity
            {
                public string ExternalId { get; private set; } = "";
                {{memberBody}}
            }
            """, References);

    private static string Repository(SourceGenRunResult result)
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var hit = sources.FirstOrDefault(kv => kv.Key.Contains("Member.Repository"));
        hit.Value.Should().NotBeNull("the repository is where the domain-key lookup is generated");
        return hit.Value!;
    }

    [Fact]
    public void AKeyNamedOnTheClass_CanIncludeTheForeignKeyTheParentGenerates()
    {
        var result = Run("""[LogicKey("WorkspaceId", nameof(ExternalId), Scope = UniquenessScope.Global)]""");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0636").Should().BeFalse(
            "WorkspaceId is the key Workspace's [Relation.OneToMany<Member>] generates on Member");
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0637").Should().BeFalse();

        var repository = Repository(result);
        repository.Should().Contain("GetByWorkspaceIdAndExternalIdAsync",
            "the parts, in the order written on the class");
        repository.Should().Contain("Guid workspaceId",
            "the generated key's type came from the relation graph, which is the only place that knows it");
        repository.Should().Contain("e.WorkspaceId == workspaceId && e.ExternalId == externalId");
    }

    [Fact]
    public void ANameThatMatchesNothing_IsPrag0636()
    {
        var result = Run("""[LogicKey("WorkspaceId", "ExternalCode")]""");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0636").Should().BeTrue(
            "ExternalCode is neither a property nor a generated key; the string form cannot be checked by the compiler");
    }

    [Fact]
    public void TheKeyDeclaredOnTheClassAndOnAProperty_IsPrag0637()
    {
        var result = Run(
            """[LogicKey("WorkspaceId", nameof(ExternalId))]""",
            """[LogicKey] public string Code { get; private set; } = "";""");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0637").Should().BeTrue(
            "one entity has one domain key, declared in one place");
    }

    /// <summary>The control: the property form is untouched by the class form's existence.</summary>
    [Fact]
    public void ThePropertyForm_StillWorksAlone()
    {
        var result = Run("", """[LogicKey] public string Code { get; private set; } = "";""");

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0636").Should().BeFalse();
        GeneratorTestHelper.HasDiagnostic(result, "PRAG0637").Should().BeFalse();
        Repository(result).Should().Contain("GetByCodeAsync");
    }
}
