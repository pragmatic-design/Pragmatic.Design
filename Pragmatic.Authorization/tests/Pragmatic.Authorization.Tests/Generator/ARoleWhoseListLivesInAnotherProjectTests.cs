using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Authorization.Tests.Generator;

/// <summary>
///     A role whose <c>DefaultPermissions</c> reads a list held in another project is generated for in
///     an IDE exactly as it is by the build.
/// </summary>
/// <remarks>
///     <para>
///         The catalogue follows <c>=&gt; Shared.Granted</c> to the field's initializer and binds it. The
///         build hands the generator a referenced project as a DLL, whose field carries no syntax; an IDE
///         hands it a project of the same solution as a <b>compilation</b>, whose field does — in a tree
///         that belongs to the other compilation. Binding it there threw, and an exception in a transform
///         discards the whole generator's output: the module shows no generated code at all (the
///         same shape in <c>ResultFeature</c> emptied Time off in Visual Studio).
///     </para>
///     <para>
///         <see cref="GeneratorTestHelper.CompileReference" /> returns the reference as a compilation —
///         the IDE's shape. <see cref="AsBuilt" /> emits the same assembly to a DLL — the build's.
///     </para>
/// </remarks>
public class ARoleWhoseListLivesInAnotherProjectTests : AuthorizationGeneratorTestBase
{
    private const string SharedLibrary = """
        namespace Company.Grants;

        public static class Shared
        {
            public static readonly string[] Granted = ["kb.read", "kb.write"];
        }
        """;

    private const string Source = """
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

    private static readonly MetadataReference AsASolutionProject =
        GeneratorTestHelper.CompileReference("Company.Grants", SharedLibrary);

    [Fact]
    public void AsAProjectOfTheSolution_TheGeneratorRunsToCompletion()
    {
        var result = RunGenerator(Source, AsASolutionProject);

        foreach (var run in result.RunResult.Results)
            run.Exception.Should().BeNull(
                $"{run.Generator.GetGeneratorType().Name} threw, and an IDE then shows the module "
                + "without any of its generated code");

        GetGeneratedSource(result, "PermissionRegistry")
            .Should().NotBeNull("the role is catalogued");
    }

    [Fact]
    public void AsAProjectOfTheSolution_TheCatalogueIsTheOneTheBuildWrites()
    {
        var inTheIde = GetGeneratedSource(RunGenerator(Source, AsASolutionProject), "PermissionRegistry");
        var inTheBuild = GetGeneratedSource(RunGenerator(Source, AsBuilt(AsASolutionProject)), "PermissionRegistry");

        inTheBuild.Should().NotBeNull("the control: the build catalogues the role");
        inTheIde.Should().Be(inTheBuild,
            "the editor has to show the code the compiler will build, not a variant of it");
    }

    /// <summary>The same assembly, as the build passes it: emitted, with no syntax behind its symbols.</summary>
    private static MetadataReference AsBuilt(MetadataReference project)
    {
        var compilation = ((CompilationReference)project).Compilation;
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        emitted.Success.Should().BeTrue("the referenced assembly has to build for the comparison to mean anything");

        return MetadataReference.CreateFromImage(image.ToArray());
    }
}
