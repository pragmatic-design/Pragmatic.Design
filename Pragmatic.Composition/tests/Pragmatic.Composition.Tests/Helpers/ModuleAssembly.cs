// Pragmatic.Composition.Tests - a module in an assembly of its own, for [IncludeModule<T>] tests.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Composition.Tests.Helpers;

/// <summary>
///     Emits an assembly that declares exactly one <c>[Module]</c>, so a test source can depend on it with
///     <c>[IncludeModule&lt;T&gt;]</c>.
/// </summary>
/// <remarks>
///     An assembly has one module (PRAG0628), so a module a test depends on cannot be declared beside
///     the module under test: it has to be a reference, as it is in an application.
/// </remarks>
internal static class ModuleAssembly
{
    /// <summary>
    ///     A reference to an assembly named <paramref name="name" /> declaring
    ///     <c>[Module(Name = "<paramref name="name" />")] public class {name}Module</c> in namespace
    ///     <paramref name="name" />.
    /// </summary>
    public static MetadataReference Named(string name)
    {
        var source = $$"""
            namespace {{name}};

            [Pragmatic.Composition.Attributes.Module(Name = "{{name}}")]
            public class {{name}}Module { }
            """;

        var runtimePath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimePath, "System.Runtime.dll")),
                MetadataReference.CreateFromFile(typeof(ModuleAttribute).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        if (!emit.Success)
            throw new InvalidOperationException(
                $"The module assembly '{name}' did not compile: "
                + string.Join("; ", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));

        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
