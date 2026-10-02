// Pragmatic.Design - Composition Detection Helper
// Shared helper to detect if Pragmatic.Composition is referenced

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen;

/// <summary>
///     Detects whether Pragmatic.Composition is referenced in the compilation.
///     Used by all generators to conditionally emit [PragmaticMetadata] attributes.
/// </summary>
internal static class CompositionDetector
{
    private const string CompositionAssemblyName = "Pragmatic.Composition.Host";
    private const string MetadataAttributeFullName = "Pragmatic.Composition.Attributes.PragmaticMetadataAttribute";
    private const string MetadataCategoryFullName = "Pragmatic.Composition.Metadata.MetadataCategory";
    private const string CompositionHostMarkerFullName = "Pragmatic.Composition.Hosting.PragmaticBuilder";

    /// <summary>
    ///     Checks if the Composition contract surface is available — true when either
    ///     Abstractions (for <c>[PragmaticMetadata]</c> attribute emission in library
    ///     mode) or Composition.Host (for host-mode aggregation) is referenced.
    /// </summary>
    public static bool IsCompositionReferenced(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName(MetadataAttributeFullName) is not null
            || compilation.GetTypeByMetadataName(CompositionHostMarkerFullName) is not null;
    }

    /// <summary>
    ///     Checks if the Composition host runtime is referenced. Required before
    ///     generating <c>PragmaticHost.g.cs</c> / <c>Host.Entry.g.cs</c>, because
    ///     those emit calls into types that live in <c>Pragmatic.Composition.Host</c>.
    ///     Referencing only Abstractions transitively is NOT enough.
    /// </summary>
    public static bool IsCompositionHostReferenced(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName(CompositionHostMarkerFullName) is not null;
    }

    /// <summary>
    ///     Gets the PragmaticMetadataAttribute type symbol if available.
    /// </summary>
    /// <param name="compilation">The compilation to check.</param>
    /// <returns>The type symbol, or null if not referenced.</returns>
    public static INamedTypeSymbol? GetMetadataAttributeSymbol(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName(MetadataAttributeFullName);
    }

    /// <summary>
    ///     Gets the MetadataCategory enum symbol if available.
    /// </summary>
    /// <param name="compilation">The compilation to check.</param>
    /// <returns>The type symbol, or null if not referenced.</returns>
    public static INamedTypeSymbol? GetMetadataCategorySymbol(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName(MetadataCategoryFullName);
    }

    /// <summary>
    ///     Checks if the project is a HOST (Exe with entry point).
    /// </summary>
    /// <param name="compilation">The compilation to check.</param>
    /// <returns>True if this is a HOST project.</returns>
    public static bool IsHostProject(Compilation compilation)
    {
        // Check OutputKind
        if (compilation.Options.OutputKind != OutputKind.ConsoleApplication &&
            compilation.Options.OutputKind != OutputKind.WindowsApplication)
            return false;

        // Check for Main method or top-level statements
        return compilation.GetEntryPoint(default) is not null;
    }

    /// <summary>
    ///     Attribute types that only exist in a test project. Probed first because they are cheap and
    ///     exact; the assembly probe below is what catches everything this list does not name.
    /// </summary>
    private static readonly string[] TestFrameworkAttributes =
    {
        "Xunit.FactAttribute",
        "Xunit.TheoryAttribute",
        "NUnit.Framework.TestAttribute",
        "NUnit.Framework.TestFixtureAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute",
        "Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute",
        "TUnit.Core.TestAttribute",
        "Microsoft.Testing.Platform.ITestApplicationBuilder"
    };

    /// <summary>
    ///     Referenced-assembly name prefixes that identify a test project regardless of which testing
    ///     API it uses. Covers the test <em>platforms</em> (VSTest, Microsoft.Testing.Platform) that any
    ///     runnable test project must reference, plus the well-known frameworks' own assemblies.
    /// </summary>
    private static readonly string[] TestAssemblyPrefixes =
    {
        "xunit",
        "nunit",
        "mstest",
        "tunit",
        "machine.specifications",
        "microsoft.visualstudio.testplatform",
        "microsoft.testplatform",
        "microsoft.testing.platform",
        "microsoft.testing.extensions",
        "microsoft.net.test.sdk"
    };

    /// <summary>
    ///     Checks if the project is a test project.
    /// </summary>
    /// <remarks>
    ///     Probing a closed list of six framework attributes meant TUnit, xunit.v3 and anything less
    ///     common were not recognised — and since those projects are executables with an entry point,
    ///     <see cref="DetermineMode" /> promoted them to <see cref="GeneratorMode.Host" /> and emitted
    ///     host wiring inside a test assembly. The assembly-name probe closes that hole: a test project
    ///     that can actually run must reference a test platform.
    /// </remarks>
    /// <param name="compilation">The compilation to check.</param>
    /// <returns>True if this is a test project.</returns>
    public static bool IsTestProject(Compilation compilation)
    {
        foreach (var typeName in TestFrameworkAttributes)
            if (compilation.GetTypeByMetadataName(typeName) is not null)
                return true;

        foreach (var reference in compilation.ReferencedAssemblyNames)
        {
            var name = reference.Name;
            foreach (var prefix in TestAssemblyPrefixes)
            {
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines the generator mode for the current compilation.
    /// </summary>
    /// <param name="compilation">The compilation to check.</param>
    /// <returns>The appropriate generator mode.</returns>
    public static GeneratorMode DetermineMode(Compilation compilation)
    {
        if (IsTestProject(compilation))
            return GeneratorMode.Skip;

        // An Exe project that doesn't reference Composition.Host can't host Pragmatic
        // modules — treat it as Library so no PragmaticHost code is emitted against
        // types the compilation cannot resolve.
        if (IsHostProject(compilation) && IsCompositionHostReferenced(compilation))
            return GeneratorMode.Host;

        return GeneratorMode.Library;
    }
}

/// <summary>
///     Generator execution mode.
/// </summary>
internal enum GeneratorMode
{
    /// <summary>Library mode: generate metadata + Add*Dependencies methods.</summary>
    Library,

    /// <summary>Host mode: aggregate metadata from referenced assemblies.</summary>
    Host,

    /// <summary>Skip mode: test project, don't generate HOST code.</summary>
    Skip
}