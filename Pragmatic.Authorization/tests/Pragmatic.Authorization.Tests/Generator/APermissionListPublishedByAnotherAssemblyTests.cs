using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     A role whose <c>DefaultPermissions</c> reads a list held in another assembly is catalogued with
///     what that list holds — when the assembly publishes it — and reported when it cannot be read.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The catalogue must not answer <c>[]</c> for such a role, which is also what a role
///         that grants nothing answers. The runtime reads the property and grants every entry, so what is
///         applied and what a role screen lists would diverge, silently. A referenced assembly is metadata:
///         there is no initializer to follow and a <c>static readonly string[]</c> has no constant value,
///         so the values have to be <em>stated</em> — <c>[PermissionSet]</c> makes the generator write
///         them into its own assembly, and this compilation reads them from there.
///     </para>
///     <para>
///         The control that makes this mean something is the unmarked list: it must still be reported
///         (PRAG1015) rather than catalogued as empty, and the role must stay in the registry.
///     </para>
/// </remarks>
public class APermissionListPublishedByAnotherAssemblyTests : AuthorizationGeneratorTestBase
{
    private const string Published = """
        using Pragmatic.Authorization;

        namespace Company.Grants;

        public static class Shared
        {
            [PermissionSet]
            public static readonly string[] Granted = ["kb.read", "kb.write"];
        }
        """;

    private const string Unpublished = """
        namespace Company.Grants;

        public static class Shared
        {
            public static readonly string[] Granted = ["kb.read", "kb.write"];
        }
        """;

    private const string Role = """
        using System.Collections.Generic;
        using Pragmatic.Authorization;

        namespace TestApp.Knowledge;

        public sealed class Steward : IRole
        {
            public static string Name => "steward";
            public static string? Description => "Curates the knowledge base";
            public static IReadOnlyList<string> DefaultPermissions => Company.Grants.Shared.Granted;
        }
        """;

    private static MetadataReference Library(string source)
        => GeneratorTestHelper.RunGeneratorAsReference<PragmaticSourceGenerator>(
            "Company.Grants", source, GetAuthorizationReferences());

    /// <summary>The same assembly as the build passes it: emitted, with no syntax behind its symbols.</summary>
    private static MetadataReference AsBuilt(MetadataReference project)
    {
        var compilation = ((CompilationReference)project).Compilation;
        using var image = new MemoryStream();
        compilation.Emit(image).Success.Should()
            .BeTrue("the referenced assembly has to build for the comparison to mean anything");

        return MetadataReference.CreateFromImage(image.ToArray());
    }

    [Fact]
    public void AMarkedList_IsPublishedByTheAssemblyThatHoldsIt()
    {
        var result = RunGenerator(Published);

        GetGeneratedSourcesAsDictionary(result).Values
            .Should().Contain(source =>
                source.Contains("PermissionSetValues(\"Company.Grants.Shared.Granted\", \"kb.read\", \"kb.write\")",
                    StringComparison.Ordinal),
                "the assembly states what its list holds, for the compilations that cannot read it");
    }

    [Fact]
    public void ARoleReadingAPublishedList_IsCataloguedWithItsPermissions()
    {
        var result = RunGenerator(Role, AsBuilt(Library(Published)));

        var registry = GetGeneratedSource(result, "PermissionRegistry");

        registry.Should().NotBeNull();
        registry!.Should().Contain("kb.read").And.Contain("kb.write");
        HasDiagnostic(result, "PRAG1015").Should().BeFalse("the values are there to be read");
    }

    [Fact]
    public void ARoleReadingAnUnpublishedList_IsReported_AndStaysCatalogued()
    {
        var result = RunGenerator(Role, AsBuilt(Library(Unpublished)));

        HasDiagnostic(result, "PRAG1015").Should()
            .BeTrue("the catalogue cannot say what the role grants, and saying 'nothing' is a falsehood");
        GetGeneratedSource(result, "PermissionRegistry").Should()
            .NotBeNull("the role is still a role: only its grant is unknown");
    }

    /// <summary>
    ///     The list a module actually writes: its own permission constants, which this same run generates.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Every case above spells string literals, and that is why this went unnoticed. A module's
    ///     permissions are constants the generator writes, so nothing binds them while the transform
    ///     reads the list: it folded to no values at all, and the feature reported <c>PRAG1016</c> —
    ///     telling the author to write the list "as a collection expression", which it was. The paths
    ///     now travel to where the catalogue is known, exactly as a role's own <c>DefaultPermissions</c>
    ///     already did.
    /// </remarks>
    [Fact]
    public void AMarkedListOfThisModulesOwnConstants_IsPublishedWithTheirValues()
    {
        const string source = """
            using System.Collections.Generic;
            using Pragmatic.Authorization;

            [assembly: Permission("kb.article.read", "Read an article")]
            [assembly: Permission("kb.article.write", "Write an article")]

            namespace Company.Grants;

            public static class Shared
            {
                [PermissionSet]
                public static IReadOnlyList<string> Granted =>
                    [KbPermissions.Article.Read, KbPermissions.Article.Write];
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1016").Should()
            .BeFalse("the values are this run's own, and the catalogue it builds has them");
        GetGeneratedSourcesAsDictionary(result).Values
            .Should().Contain(generated => generated.Contains(
                    "PermissionSetValues(\"Company.Grants.Shared.Granted\", \"kb.article.read\", \"kb.article.write\")",
                    StringComparison.Ordinal),
                "what travels in metadata is the values, not the constant paths — the reading assembly "
                + "has no such class");
    }

    /// <summary>
    ///     The other half: a list marked and unreadable publishes nothing, and says so where it is declared.
    ///     Without this the author marks the list, nothing happens, and the only complaint appears in the
    ///     other assembly — about the role.
    /// </summary>
    [Fact]
    public void AMarkedListTheGeneratorCannotRead_IsReportedWhereItIsDeclared()
    {
        const string source = """
            using System.Linq;
            using Pragmatic.Authorization;

            namespace Company.Grants;

            public static class Shared
            {
                [PermissionSet]
                public static string[] Granted => Build();

                private static string[] Build() => ["kb.read"];
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG1016").Should().BeTrue();
        GetGeneratedSourcesAsDictionary(result).Values
            .Should().NotContain(generated => generated.Contains("PermissionSetValues", StringComparison.Ordinal),
                "publishing an empty list would answer 'grants nothing' with the generator's authority");
    }

    /// <summary>
    ///     The control: a list in the role's own compilation is read from its initializer as before, with
    ///     no attribute and no diagnostic.
    /// </summary>
    [Fact]
    public void AListInTheRolesOwnAssembly_IsStillReadFromItsInitializer()
    {
        const string source = """
            using System.Collections.Generic;
            using Pragmatic.Authorization;

            namespace TestApp.Knowledge;

            public static class Grants
            {
                public static readonly string[] Granted = ["kb.read", "kb.write"];
            }

            public sealed class Steward : IRole
            {
                public static string Name => "steward";
                public static string? Description => "Curates the knowledge base";
                public static IReadOnlyList<string> DefaultPermissions => Grants.Granted;
            }
            """;

        var result = RunGenerator(source);

        GetGeneratedSource(result, "PermissionRegistry")!.Should().Contain("kb.read");
        HasDiagnostic(result, "PRAG1015").Should().BeFalse();
    }
}
