using System.Reflection;
using System.Runtime.Loader;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.SourceGenerator.Tests.Features.Logging;

/// <summary>A source with call sites, generated, compiled and loaded, so a test can call them.</summary>
/// <remarks>
///     What a call site does is only visible by running it: the generated text can say the right thing
///     and the state can still render the wrong one. Each instance loads into its own collectible
///     context, so tests do not see each other's types.
/// </remarks>
internal sealed class CompiledCallSites
{
    private readonly Assembly _assembly;

    public CompiledCallSites(string source)
    {
        var result = LogCallSiteTestSources.Run(source);
        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty();

        using var image = new MemoryStream();
        var emitted = result.OutputCompilation.Emit(image);
        emitted.Success.Should().BeTrue();

        _assembly = new AssemblyLoadContext(null, isCollectible: true).LoadFromStream(new MemoryStream(image.ToArray()));
    }

    /// <summary>A type of the compiled source.</summary>
    public Type Type(string typeName) => _assembly.GetType(typeName, throwOnError: true)!;

    /// <summary>Calls a static method of <paramref name="typeName" />.</summary>
    public void Call(string typeName, string method, params object?[] arguments)
        => Type(typeName)
            .GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments);
}
