// Pragmatic.Composition.SourceGenerator - Module dependency validator

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Validates module dependencies declared with <c>[IncludeModule&lt;TModule&gt;]</c> (MODDEP-DEAD — PRAG1601/PRAG1602 were
///     declared but only ever emitted for the service-level DI graph, never for module dependencies):
///     PRAG1601 for a dependency that resolves to no known module, PRAG1602 for a module dependency cycle.
/// </summary>
internal static class ModuleDependencyValidator
{
    public static void Validate(
        SourceProductionContext context,
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> discoveredModules,
        ImmutableArray<DiscoveredModuleInfo> domainModules)
    {
        if (localModules.IsDefaultOrEmpty)
            return;

        if (!localModules.Any(m => !m.DependsOn.IsDefaultOrEmpty))
            return;

        // Known module names across the whole composition (local + referenced).
        var known = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in localModules)
            known.Add(m.Name);
        foreach (var m in discoveredModules)
            known.Add(m.Name);
        foreach (var m in domainModules)
            known.Add(m.Name);

        // PRAG1601 — a declared dependency that matches no known module (typically a typo).
        foreach (var m in localModules)
            foreach (var dep in m.DependsOn)
                if (!known.Contains(dep))
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.ModuleDependencyNotDeclared,
                        m.Location ?? Location.None, m.Name, dep));

        DetectCycles(context, localModules);
    }

    // PRAG1602 — cycle among local modules' DependsOn edges (only edges to other local modules; a
    // reference DAG cannot form a cycle across assemblies).
    private static void DetectCycles(SourceProductionContext context, ImmutableArray<ModuleModel> localModules)
    {
        var byName = new Dictionary<string, ModuleModel>(System.StringComparer.Ordinal);
        foreach (var m in localModules)
            byName[m.Name] = m;

        var state = new Dictionary<string, int>(System.StringComparer.Ordinal); // 0=unvisited,1=in-stack,2=done
        var reported = false;

        foreach (var m in localModules)
        {
            if (reported)
                break;
            if (!state.ContainsKey(m.Name))
                reported = Visit(context, m.Name, byName, state, new List<string>());
        }
    }

    private static bool Visit(
        SourceProductionContext context,
        string name,
        Dictionary<string, ModuleModel> byName,
        Dictionary<string, int> state,
        List<string> path)
    {
        state[name] = 1;
        path.Add(name);

        if (byName.TryGetValue(name, out var module))
            foreach (var dep in module.DependsOn)
            {
                if (!byName.ContainsKey(dep))
                    continue; // cross-assembly or unresolved (unresolved → PRAG1601 already)

                if (state.TryGetValue(dep, out var s) && s == 1)
                {
                    // Cycle: dep is on the current stack.
                    var start = path.IndexOf(dep);
                    var cycle = start >= 0 ? path.Skip(start).ToList() : new List<string> { dep };
                    cycle.Add(dep);
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.CircularDependency,
                        module.Location ?? Location.None, string.Join(" -> ", cycle)));
                    return true;
                }

                if (!state.ContainsKey(dep) && Visit(context, dep, byName, state, path))
                    return true;
            }

        path.RemoveAt(path.Count - 1);
        state[name] = 2;
        return false;
    }
}
