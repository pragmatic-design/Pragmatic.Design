using Microsoft.CodeAnalysis;
using Pragmatic.Result;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Result;

/// <summary>
///     An error that inherits <c>Code</c>, <c>StatusCode</c> or <c>Title</c> from a base in another
///     project is generated for in an IDE exactly as it is by the build.
/// </summary>
/// <remarks>
///     <para>
///         The build hands the generator a referenced project as a DLL; an IDE hands it a project of the
///         same solution as a <b>compilation</b>, whose symbols still carry their declaring syntax. The
///         schema registration reads those three members from an expression body up the base chain, and
///         asking for the semantic model of a tree that belongs to the other compilation throws an
///         <c>ArgumentException</c> and discards the whole generator's output. Errors that derive from
///         <c>Error</c> and inherit <c>Title =&gt; string.Empty</c> are enough: in an IDE the module
///         would get no generated code at all while <c>dotnet build</c> compiles it.
///     </para>
///     <para>
///         <see cref="GeneratorTestHelper.CompileReference" /> returns the reference as a compilation —
///         the IDE's shape. <see cref="AsBuilt" /> emits the same assembly to a DLL — the build's.
///     </para>
/// </remarks>
public class AnErrorWhoseBaseLivesInAnotherProjectTests
{
    private const string BaseLibrary = """
        using Pragmatic.Result;

        namespace Company.Errors;

        public abstract record CompanyRuleError : Error
        {
            public override int StatusCode => 409;
            public override string Title => "Company rule";
        }
        """;

    private const string Source = """
        using Company.Errors;

        namespace TestApp.Errors;

        public sealed partial record ClashError : CompanyRuleError
        {
            public override string Code => "CLASH";
        }
        """;

    // IError is in Pragmatic.Abstractions, the Error base record in Pragmatic.Result.
    private static readonly MetadataReference[] ResultAssemblies =
    [
        GeneratorTestHelper.FromType<IError>(),
        GeneratorTestHelper.FromTypeAssembly(typeof(Error))
    ];

    private static readonly MetadataReference AsASolutionProject =
        GeneratorTestHelper.CompileReference("Company.Errors", BaseLibrary, ResultAssemblies);

    [Fact]
    public void AsAProjectOfTheSolution_TheGeneratorRunsToCompletion()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, [.. ResultAssemblies, AsASolutionProject]);

        foreach (var run in result.RunResult.Results)
            run.Exception.Should().BeNull(
                $"{run.Generator.GetGeneratorType().Name} threw, and an IDE then shows the module "
                + "without any of its generated code");

        Registration(result).Should().NotBeNull("the error is registered with the runtime registries");
    }

    [Fact]
    public void AsAProjectOfTheSolution_TheRegistrationIsTheOneTheBuildWrites()
    {
        var inTheIde = Registration(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, [.. ResultAssemblies, AsASolutionProject]));
        var inTheBuild = Registration(GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            Source, [.. ResultAssemblies, AsBuilt(AsASolutionProject)]));

        inTheBuild.Should().NotBeNull("the control: the build registers the error");
        inTheIde.Should().Be(inTheBuild,
            "the editor has to show the code the compiler will build, not a variant of it");
    }

    private static string? Registration(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Where(kv => kv.Key.Contains("ClashError.ErrorRegistration"))
            .Select(kv => kv.Value)
            .FirstOrDefault();

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
