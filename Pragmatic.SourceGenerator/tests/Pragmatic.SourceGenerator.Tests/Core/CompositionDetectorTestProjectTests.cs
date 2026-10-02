using System.IO;
using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <c>CompositionDetector.IsTestProject</c> gates <c>DetermineMode</c>. When it says "no", an
///     executable that references Composition.Host is promoted to host mode — so a modern test project
///     (xunit.v3, TUnit, anything on Microsoft.Testing.Platform: all of them are executables) would
///     get host wiring generated inside it if the detector knew only a closed list of attribute names.
/// </summary>
public class CompositionDetectorTestProjectTests
{
    [Fact]
    public void IsTestProject_XUnitV2Attribute_ReturnsTrue()
        => IsTestProject("namespace Xunit { public sealed class FactAttribute : System.Attribute { } }")
            .Should().BeTrue();

    // The regression: TUnit's marker was not in the list, so the project looked like an app.
    [Fact]
    public void IsTestProject_TUnitAttribute_ReturnsTrue()
        => IsTestProject("namespace TUnit.Core { public sealed class TestAttribute : System.Attribute { } }")
            .Should().BeTrue();

    [Fact]
    public void IsTestProject_MicrosoftTestingPlatform_ReturnsTrue()
        => IsTestProject("namespace Microsoft.Testing.Platform { public interface ITestApplicationBuilder { } }")
            .Should().BeTrue();

    // xunit.v3 keeps the Xunit.FactAttribute name, but a framework nobody enumerated would not: the
    // referenced test platform assembly is what makes the verdict framework-agnostic.
    [Theory]
    [InlineData("xunit.v3.core")]
    [InlineData("TUnit.Engine")]
    [InlineData("Microsoft.Testing.Platform")]
    [InlineData("Microsoft.TestPlatform.TestHost")]
    public void IsTestProject_ReferencesATestPlatformAssembly_ReturnsTrue(string assemblyName)
        => IsTestProject("namespace App { public class Nothing { } }", CompileEmptyAssembly(assemblyName))
            .Should().BeTrue();

    [Fact]
    public void IsTestProject_OrdinaryApplication_ReturnsFalse()
        => IsTestProject("namespace App { public class Program { } }").Should().BeFalse();

    // The broadened probe must not turn every referenced library into evidence of a test project.
    [Fact]
    public void IsTestProject_UnrelatedAssembly_ReturnsFalse()
        => IsTestProject("namespace App { public class Program { } }", CompileEmptyAssembly("Contoso.Billing"))
            .Should().BeFalse();

    private static bool IsTestProject(string source, params MetadataReference[] extraReferences)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        MetadataReference[] references =
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll")),
            .. extraReferences
        ];

        var compilation = CSharpCompilation.Create(
            "SubjectAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "TestSource.cs")],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return CompositionDetector.IsTestProject(compilation);
    }

    /// <summary>Emits a real (empty) assembly under the given name, so it appears in ReferencedAssemblyNames.</summary>
    private static MetadataReference CompileEmptyAssembly(string assemblyName)
    {
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText("public class Marker { }")],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(Path.Combine(runtimeDirectory, "System.Runtime.dll"))
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue("the stand-in assembly '{0}' must compile", assemblyName);
        stream.Position = 0;

        return MetadataReference.CreateFromStream(stream);
    }
}
