// Pragmatic.SourceGen.Testing - Generator Test Helper
// This file is linked into test projects, not compiled directly.

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen.Testing;

public static partial class GeneratorTestHelper
{
    /// <summary>
    ///     Runs a source generator on a compilation over the given framework references instead of the runtime the
    ///     test runs on: a consumer on another target framework, such as <c>net11.0</c> from a <c>net10.0</c> test.
    /// </summary>
    /// <remarks>
    ///     The generator writes some forms only for a consumer that can see the API they call (#834); this is how a
    ///     test shows both shapes. The framework references replace the base set entirely, so
    ///     <paramref name="additionalReferences" /> should carry package assemblies only: an assembly the framework
    ///     already provides, added again from the test's own runtime, would be a second, older copy.
    /// </remarks>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="frameworkReferences">The target framework's reference assemblies.</param>
    /// <param name="additionalReferences">Package assemblies the source uses.</param>
    public static SourceGenRunResult RunGeneratorOnFramework<TGenerator>(
        string source,
        IEnumerable<MetadataReference> frameworkReferences,
        params MetadataReference[] additionalReferences)
        where TGenerator : IIncrementalGenerator, new()
    {
        return RunGenerator(new TGenerator(), source, OutputKind.DynamicallyLinkedLibrary, additionalReferences,
            frameworkReferences: frameworkReferences);
    }
}
