using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     What a dotted <c>Target</c> path does when the navigation on the way is not there.
/// </summary>
/// <remarks>
///     <para>
///         One method served both <c>ToEntity</c> and <c>ApplyTo</c> and chose by asking whether the
///         intermediate type had a parameterless constructor — a fact about the type, not about the
///         write. Building an aggregate and updating one are different jobs: on a create every
///         navigation is missing by definition, on an update a missing one means it was never loaded,
///         or is genuinely absent.
///     </para>
///     <para>
///         The two outcomes it produced were both wrong on an update. <c>??= new()</c> wrote into a
///         brand-new instance — an insert dressed as an update. The other branch skipped the
///         assignment and reported success.
///     </para>
/// </remarks>
public class NestedTargetWriteTests : MappingGeneratorTestBase
{
    /// <param name="recipientDeclaration">The intermediate the target path walks through.</param>
    private static string Source(string recipientDeclaration) => $$"""
        using System;
        using Pragmatic.Mapping.Attributes;

        // Declared here rather than referenced: the analyzer matches [Entity] by name, and this test
        // project does not carry Pragmatic.Persistence.
        namespace Pragmatic.Persistence.Entity
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class EntityAttribute : Attribute { }
        }

        namespace TestApp.Entities
        {
            {{recipientDeclaration}}

            public class Shipment
            {
                public string TrackingNumber { get; set; } = "";
                public Recipient Recipient { get; set; } = new();
            }
        }

        namespace TestApp.Dtos
        {
            [MapTo<TestApp.Entities.Shipment>]
            public partial class UpdateShipmentDto
            {
                public string? TrackingNumber { get; init; }

                [MapProperty(Target = "Recipient.City")]
                public string? City { get; init; }
            }
        }
        """;

    /// <summary>A shape the parent owns — no identity, no row of its own.</summary>
    private const string OwnedRecipient = """
        public class Recipient
        {
            public string City { get; set; } = "";
        }
        """;

    /// <summary>A row that exists independently of the shipment.</summary>
    private const string EntityRecipient = """
        [global::Pragmatic.Persistence.Entity.EntityAttribute]
        public class Recipient
        {
            public string City { get; set; } = "";
        }
        """;

    // ── Creating builds the graph ────────────────────────────────────────────

    /// <summary>
    ///     On a create there is nothing to preserve, so the path is built as it is walked.
    /// </summary>
    [Fact]
    public void ToEntity_BuildsTheIntermediatesOnTheWay()
    {
        var result = RunGenerator(Source(OwnedRecipient));

        NoCompilationErrors(result);

        ToEntityBody(GetGeneratedSource(result, "UpdateShipmentDto.Mapping"))
            .Should().Contain("entity.Recipient ??= new();");
    }

    /// <summary>
    ///     Even when the intermediate is an entity: a create makes the whole aggregate.
    /// </summary>
    [Fact]
    public void ToEntity_BuildsThemEvenWhenTheyAreEntities()
    {
        var result = RunGenerator(Source(EntityRecipient));

        NoCompilationErrors(result);

        ToEntityBody(GetGeneratedSource(result, "UpdateShipmentDto.Mapping"))
            .Should().Contain("entity.Recipient ??= new();");
    }

    // ── Updating writes into what is there ───────────────────────────────────

    /// <summary>
    ///     A shape the parent owns is still built on an update: it is part of this aggregate.
    /// </summary>
    [Fact]
    public void ApplyTo_StillBuildsAShapeTheParentOwns()
    {
        var result = RunGenerator(Source(OwnedRecipient));

        NoCompilationErrors(result);

        ApplyToBody(GetGeneratedSource(result, "UpdateShipmentDto.Mapping"))
            .Should().Contain("entity.Recipient ??= new();");
    }

    /// <summary>
    ///     An entity on the path is never created by an update — that would be an insert.
    /// </summary>
    [Fact]
    public void ApplyTo_NeverCreatesAnEntityOnTheWay()
    {
        var result = RunGenerator(Source(EntityRecipient));

        NoCompilationErrors(result);

        ApplyToBody(GetGeneratedSource(result, "UpdateShipmentDto.Mapping"))
            .Should().NotContain("??= new()",
                "creating a row because someone wrote a dotted path is an insert nobody asked for");
    }

    /// <summary>
    ///     And it does not skip either: a navigation that is not there is said out loud.
    /// </summary>
    /// <remarks>
    ///     The branch this replaces reported success while changing nothing in the database.
    /// </remarks>
    [Fact]
    public void ApplyTo_SaysSoInsteadOfSkipping()
    {
        var result = RunGenerator(Source(EntityRecipient));

        var body = ApplyToBody(GetGeneratedSource(result, "UpdateShipmentDto.Mapping"));

        body.Should().Contain("if (entity.Recipient is null)");
        body.Should().Contain("InvalidOperationException");
        body.Should().Contain("EagerLoad",
            "naming the way out is what makes the message worth throwing");
    }

    // ── Helper ───────────────────────────────────────────────────────────────

    private static string ToEntityBody(string? generated)
    {
        var start = Index(generated, "public global::TestApp.Entities.Shipment ToEntity");
        var end = Index(generated, "public void ApplyTo");
        return generated![start..end];
    }

    private static string ApplyToBody(string? generated)
    {
        return generated![Index(generated, "public void ApplyTo")..];
    }

    private static int Index(string? generated, string marker)
    {
        var at = generated?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
        at.Should().BeGreaterThan(-1, $"the generated source should contain '{marker}'");
        return at;
    }

    private static void NoCompilationErrors(SourceGenRunResult result)
    {
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }
}
