// Pragmatic.Composition.Tests - Generator Test Helper
// Thin wrapper over shared GeneratorTestHelper with Composition-specific references.

using Microsoft.CodeAnalysis;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Attributes;
using Pragmatic.Events;
using Pragmatic.Events.Attributes;
using Pragmatic.SourceGenerator;

namespace Pragmatic.Composition.Tests.Helpers;

/// <summary>
///     Helper for running PragmaticSourceGenerator tests.
/// </summary>
internal static class GeneratorTestHelper
{
    /// <summary>
    ///     Runs the PragmaticSourceGenerator on the given source code.
    /// </summary>
    /// <param name="source">The C# source code to compile.</param>
    /// <param name="extraReferences">Further assemblies the source sees — e.g. <see cref="ModuleAssembly" />.</param>
    /// <returns>A tuple containing generated outputs and diagnostics.</returns>
    public static (Dictionary<string, string> Output, IReadOnlyList<Diagnostic> Diagnostics) RunGenerator(
        string source, params MetadataReference[] extraReferences)
    {
        var result = Run(source, [], extraReferences);

        var output = Pragmatic.SourceGen.Testing.GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var diagnostics = result.Diagnostics.ToList();

        return (output, diagnostics);
    }

    /// <summary>
    ///     Runs the generator on <paramref name="source" />, which also sees the assemblies that define
    ///     <paramref name="referencedTypes" />, and returns the errors in <paramref name="source" /> itself.
    /// </summary>
    /// <remarks>
    ///     A type the source names and the compilation cannot resolve still reaches the generator, under the
    ///     name as written: a test on names is only a test on real types if the source binds.
    /// </remarks>
    public static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<Diagnostic> SourceErrors) RunGenerator(
        string source, IEnumerable<Type> referencedTypes)
    {
        var result = Run(source, referencedTypes, []);

        var generated = result.GeneratedTrees.ToHashSet();
        var sourceErrors = Pragmatic.SourceGen.Testing.GeneratorTestHelper.GetCompilationErrors(result)
            .Where(d => d.Location.SourceTree is { } tree && !generated.Contains(tree))
            .ToList();

        return (result.Diagnostics.ToList(), sourceErrors);
    }

    private static Pragmatic.SourceGen.Testing.SourceGenRunResult Run(
        string source, IEnumerable<Type> referencedTypes, MetadataReference[] extraReferences)
    {
        var references = GetCompositionReferences()
            .Concat(referencedTypes.Select(Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromTypeAssembly))
            .Concat(extraReferences)
            .ToArray();

        return Pragmatic.SourceGen.Testing.GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, references);
    }

    private static MetadataReference[] GetCompositionReferences()
    {
        var references = new List<MetadataReference>
        {
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromType<StartupStepAttribute>(),
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromType<IStartupStep>(),
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromType<ServiceAttribute>(),
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromType<EventHandlerAttribute>(),
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.FromType<IDomainEventHandler<IDomainEvent>>()
        };

        // Try to add ASP.NET Core references if available
        var aspNetCore = Pragmatic.SourceGen.Testing.GeneratorTestHelper.TryGetAssemblyReference("Microsoft.AspNetCore.Builder");
        if (aspNetCore != null)
            references.Add(aspNetCore);

        var diAbstractions =
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.TryGetAssemblyReference(
                "Microsoft.Extensions.DependencyInjection.Abstractions");
        if (diAbstractions != null)
            references.Add(diAbstractions);

        var hostingAbstractions =
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.TryGetAssemblyReference("Microsoft.Extensions.Hosting.Abstractions");
        if (hostingAbstractions != null)
            references.Add(hostingAbstractions);

        var configAbstractions =
            Pragmatic.SourceGen.Testing.GeneratorTestHelper.TryGetAssemblyReference(
                "Microsoft.Extensions.Configuration.Abstractions");
        if (configAbstractions != null)
            references.Add(configAbstractions);

        return references.ToArray();
    }
}