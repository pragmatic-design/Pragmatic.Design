using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     The catalogue and the runtime have to describe the same set, whichever shape the role used.
/// </summary>
/// <remarks>
///     <para>
///         <c>DefaultPermissions</c> was read from the <b>syntax</b> of the property, and only two
///         shapes were recognised: a collection expression and a <c>new[] { … }</c>. A role whose list
///         comes from a static field came out as <c>[]</c> — an empty grant in the catalogue — while
///         the runtime, which reads the property, granted the four it names. What is applied and what
///         is catalogued diverged, and the screen listing what a role grants stated a falsehood.
///     </para>
///     <para>
///         ⚠️ Nothing failed. The runtime is correct, so no request is refused that should be allowed;
///         only the catalogue lies, and nobody compared the two. These tests compare them: the subject
///         is the generated registry, and the expectation is the set the declaration names.
///     </para>
/// </remarks>
public class WhatARoleGrantsTests : AuthorizationGeneratorTestBase
{
    private static string? Registry(SourceGenRunResult result)
        => GetGeneratedSource(result, "PermissionRegistry");

    private const string Constants = """
        public static class Perms
        {
            public const string Read = "kb.read";
            public const string Write = "kb.write";
            public const string Retire = "kb.retire";
            public const string Curate = "kb.curate";
        }
        """;

    /// <summary>
    ///     The control: an inline collection expression was always read, and still is.
    /// </summary>
    /// <remarks>
    ///     It is the shape an example shows, which is why the defect survived. Without it, "the field
    ///     form is read" would be satisfied by a reader that returns the same four for anything.
    /// </remarks>
    [Fact]
    public void AnInlineCollectionExpression_IsCatalogued()
    {
        var registry = Registry(Run("""
            public sealed class Steward : IRole
            {
                public static string Name => "steward";
                public static string? Description => "Curates the knowledge base";
                public static IReadOnlyList<string> DefaultPermissions =>
                    [Perms.Read, Perms.Write, Perms.Retire, Perms.Curate];
            }
            """));

        registry.Should().NotBeNull();
        TheGrantOf(registry!).Should().Be("\"kb.read\", \"kb.write\", \"kb.retire\", \"kb.curate\"");
    }

    /// <summary>And a list held in a static field grants the same four.</summary>
    [Fact]
    public void AListHeldInAStaticField_IsCataloguedAsTheSameSet()
    {
        var registry = Registry(Run("""
            public sealed class Steward : IRole
            {
                private static readonly string[] Granted =
                    [Perms.Read, Perms.Write, Perms.Retire, Perms.Curate];

                public static string Name => "steward";
                public static string? Description => "Curates the knowledge base";
                public static IReadOnlyList<string> DefaultPermissions => Granted;
            }
            """));

        registry.Should().NotBeNull();
        TheGrantOf(registry!).Should().Be(
            "\"kb.read\", \"kb.write\", \"kb.retire\", \"kb.curate\"",
            "the runtime reads the property and grants four: a catalogue saying anything else is a "
            + "screen that states a falsehood");
    }

    /// <summary>The same, through a static property rather than a field.</summary>
    /// <remarks>
    ///     The two are one shape as far as the declaration is concerned — "the list lives elsewhere" —
    ///     and reading one and not the other would leave half the defect in place.
    /// </remarks>
    [Fact]
    public void AListHeldInAStaticProperty_IsCataloguedAsTheSameSet()
    {
        var registry = Registry(Run("""
            public sealed class Steward : IRole
            {
                private static IReadOnlyList<string> Granted =>
                    [Perms.Read, Perms.Write, Perms.Retire, Perms.Curate];

                public static string Name => "steward";
                public static string? Description => "Curates the knowledge base";
                public static IReadOnlyList<string> DefaultPermissions => Granted;
            }
            """));

        registry.Should().NotBeNull();
        TheGrantOf(registry!).Should().Be("\"kb.read\", \"kb.write\", \"kb.retire\", \"kb.curate\"");
    }

    /// <summary>
    ///     The second control: a role that grants nothing is catalogued as granting nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "the field form is read" is satisfied by a reader that invents a set whenever it
    ///     cannot find one, and an empty grant would become unrepresentable.
    /// </remarks>
    [Fact]
    public void ARoleThatGrantsNothing_IsCataloguedAsEmpty()
    {
        var registry = Registry(Run("""
            public sealed class Bystander : IRole
            {
                public static string Name => "bystander";
                public static string? Description => "Grants nothing";
                public static IReadOnlyList<string> DefaultPermissions => [];
            }
            """));

        registry.Should().NotBeNull();
        registry!.Should().Contain("\"bystander\"");
        TheGrantOf(registry!).Should().BeEmpty();
    }

    /// <summary>
    ///     A spread of another role's list — the way a hand-written role inherits — is catalogued as the union.
    /// </summary>
    /// <remarks>
    ///     The collection expression was read element by element, and a spread is not an element: it
    ///     was skipped in silence. Time off catalogued its manager as granting one permission while the runtime
    ///     granted eight.
    /// </remarks>
    [Fact]
    public void ASpreadOfAnotherHandWrittenRole_IsCataloguedAsTheUnion()
    {
        var registry = Registry(Run("""
            public sealed class Reader : IRole
            {
                public static string Name => "reader";
                public static string? Description => "Reads";
                public static IReadOnlyList<string> DefaultPermissions => [Perms.Read, Perms.Write];
            }

            public sealed class Curator : IRole
            {
                public static string Name => "curator";
                public static string? Description => "Curates";
                public static IReadOnlyList<string> DefaultPermissions => [.. Reader.DefaultPermissions, Perms.Curate];
            }
            """));

        registry.Should().NotBeNull();
        GrantOf(registry!, "curator").Should().Be("\"kb.read\", \"kb.write\", \"kb.curate\"");
    }

    /// <summary>A spread of a <c>[Role]</c> class — whose list the generator writes — is its flattened list.</summary>
    /// <remarks>
    ///     Compared as a set: that list is resolved after the hand-written role is read, and joins it then — the
    ///     catalogue's order was never the declaration's once a value comes from the catalogue.
    /// </remarks>
    [Fact]
    public void ASpreadOfADeclaredRole_IsCataloguedAsTheUnion()
    {
        var registry = Registry(Run("""
            [Role("reader", "Reads")]
            [Grants(Perms.Read, Perms.Write)]
            public partial class Reader;

            public sealed class Curator : IRole
            {
                public static string Name => "curator";
                public static string? Description => "Curates";
                public static IReadOnlyList<string> DefaultPermissions => [.. Reader.DefaultPermissions, Perms.Curate];
            }
            """));

        registry.Should().NotBeNull();
        GrantOf(registry!, "curator").Split(", ").Should().BeEquivalentTo(["\"kb.read\"", "\"kb.write\"", "\"kb.curate\""]);
    }

    /// <summary>A spread the catalogue cannot follow is reported, not dropped: the runtime grants more than it lists.</summary>
    [Fact]
    public void ASpreadTheCatalogueCannotRead_IsReported()
    {
        var result = Run("""
            public static class Grants
            {
                public static IReadOnlyList<string> Computed() => [Perms.Read];
            }

            public sealed class Curator : IRole
            {
                public static string Name => "curator";
                public static string? Description => "Curates";
                public static IReadOnlyList<string> DefaultPermissions => [.. Grants.Computed(), Perms.Curate];
            }
            """);

        result.RunResult.Diagnostics.Where(d => d.Id == "PRAG1013").Select(d => d.GetMessage())
            .Should().Contain(m => m.Contains("Curator") && m.Contains("Grants.Computed()"));
    }

    /// <summary>The permission list of one role inside the generated registry.</summary>
    private static string GrantOf(string registry, string role)
    {
        var start = registry.IndexOf($"new RoleInfo(\"{role}\"", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, $"the registry lists the role '{role}'");
        var open = registry.IndexOf(", [", start, System.StringComparison.Ordinal);
        var close = registry.IndexOf(']', open);
        return registry[(open + 3)..close];
    }

    /// <summary>The permission list inside the generated <c>RoleInfo</c>.</summary>
    /// <remarks>
    ///     The registry is the subject because it is what the runtime consumes; a model-level check
    ///     would pass for a set that never reaches the catalogue.
    /// </remarks>
    private static string TheGrantOf(string registry)
    {
        var open = registry.IndexOf(", [", System.StringComparison.Ordinal);
        open.Should().BeGreaterThan(-1, "the RoleInfo carries a permission list at all");

        var close = registry.IndexOf(']', open);

        return registry[(open + 3)..close];
    }

    private static SourceGenRunResult Run(string role) => RunGenerator(
        $$"""
        using System.Collections.Generic;
        using Pragmatic.Authorization;

        namespace TestApp.Knowledge;

        {{Constants}}

        {{role}}
        """);
}
