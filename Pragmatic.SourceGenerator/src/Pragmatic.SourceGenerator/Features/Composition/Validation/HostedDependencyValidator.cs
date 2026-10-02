using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     PRAG1603: every module the host hosts has its <c>[IncludeModule&lt;T&gt;]</c> dependencies hosted
///     too, or declared remote.
/// </summary>
/// <remarks>
///     <para>
///         The host registers the assemblies <c>BuildIncludedModuleAssemblies</c> returns and nothing else, so
///         a dependency outside that set compiles and fails when first resolved. The same set answers the
///         question here, so the check and the registration cannot disagree.
///     </para>
///     <para>
///         ⚠️ A remote module's dependencies are not checked: it runs in another host, which answers for
///         them. And a dependency that names no known module is <c>PRAG1601</c>'s, not reported twice.
///     </para>
/// </remarks>
internal static class HostedDependencyValidator
{
    /// <remarks>
    ///     <c>hostedAssemblies</c> is what the host registers — its own assembly, local modules,
    ///     <c>[Include]</c>s and <c>[RemoteBoundary]</c>s — and <c>null</c> when the host declares no
    ///     topology and hosts everything.
    /// </remarks>
    public static void Validate(
        SourceProductionContext context,
        HashSet<string>? hostedAssemblies,
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> discoveredModules,
        ImmutableArray<RemoteBoundaryModel> remoteBoundaries)
    {
        if (hostedAssemblies is null)
            return;

        var remoteAssemblies = new HashSet<string>(
            remoteBoundaries.IsDefaultOrEmpty
                ? Enumerable.Empty<string>()
                : remoteBoundaries.Where(r => r.AssemblyName is not null).Select(r => r.AssemblyName!),
            System.StringComparer.Ordinal);

        // Where each known module lives, by the name a dependency uses.
        var assemblyByModule = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var m in discoveredModules)
            if (!string.IsNullOrEmpty(m.AssemblyName))
                assemblyByModule[m.Name] = m.AssemblyName;
        foreach (var m in localModules)
            if (!string.IsNullOrEmpty(m.AssemblyName))
                assemblyByModule[m.Name] = m.AssemblyName!;

        var host = localModules.FirstOrDefault(m => !m.HostIncludes.IsDefaultOrEmpty || !m.RemoteBoundaries.IsDefaultOrEmpty)
                   ?? localModules.FirstOrDefault();
        var hostName = host?.Name ?? "host";
        var location = host?.Location ?? Location.None;

        // The modules that run here: hosted and not remote.
        var runHere = new List<(string Name, IEnumerable<string> DependsOn)>();
        foreach (var m in discoveredModules)
            if (hostedAssemblies.Contains(m.AssemblyName) && !remoteAssemblies.Contains(m.AssemblyName))
                runHere.Add((m.Name, m.DependsOn));
        foreach (var m in localModules)
            runHere.Add((m.Name, m.DependsOn));

        var reported = new HashSet<(string, string)>();
        foreach (var (module, dependsOn) in runHere)
            foreach (var dependency in dependsOn)
            {
                if (!assemblyByModule.TryGetValue(dependency, out var dependencyAssembly))
                    continue;

                if (hostedAssemblies.Contains(dependencyAssembly) || !reported.Add((module, dependency)))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.HostedModuleDependencyNotHosted, location, hostName, module, dependency));
            }
    }
}
