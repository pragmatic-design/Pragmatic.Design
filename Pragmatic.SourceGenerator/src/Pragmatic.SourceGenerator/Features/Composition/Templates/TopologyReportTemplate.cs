// Pragmatic.Composition.SourceGenerator - Topology Report Template

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates PragmaticTopology.g.cs with module topology report (Debug only).
/// </summary>
internal sealed class TopologyReportTemplate : CSharpTemplate
{
    private readonly HostAggregationModel _model;

    public TopologyReportTemplate(HostAggregationModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact("Host.Topology.g.cs", ToSourceText());
    }

    public override void RenderFile()
    {
        AppendNamespace(_model.RootNamespace);
        AppendLine();

        XmlSummary("Topology report for Pragmatic modules (Debug build only).");

        Class("PragmaticTopology", RenderClassBody,
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        var report = BuildReport();

        XmlSummary("Full topology report of discovered modules and their registrations.");

        AppendLine("public const string Report = \"\"\"");
        foreach (var line in report.Split('\n'))
            AppendLine($"    {line.TrimEnd()}");
        AppendLine("\"\"\";");
    }

    // StringBuilder here builds the *content* of the Report const string, not C# source code.
    // CSharpTemplate (AppendLine) is used for the C# code structure — this is correct usage.
    private string BuildReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Pragmatic Application Topology");
        sb.AppendLine("==============================");
        sb.AppendLine();

        // Module dependency graph (if modules discovered)
        if (!_model.DiscoveredModules.IsDefaultOrEmpty)
        {
            sb.AppendLine($"Module Dependency Graph ({_model.DiscoveredModules.Length} modules):");
            sb.AppendLine("----------------------------------------");

            // Build topologically sorted list
            var sorted = TopologicalSort(_model.DiscoveredModules.AsImmutableArray());

            foreach (var module in sorted)
            {
                var versionStr = module.Version is not null ? $" (v{module.Version})" : "";
                sb.AppendLine($"  [{module.Name}]{versionStr}");

                if (!module.DependsOn.IsDefaultOrEmpty)
                    sb.AppendLine($"    DependsOn: {string.Join(", ", module.DependsOn)}");

                if (module.DiRegistrationMethod is not null)
                    sb.AppendLine($"    DI: {module.DiRegistrationMethod}()");

                if (module.StartupRegistrationMethod is not null)
                    sb.AppendLine($"    Startup: {module.StartupRegistrationMethod}()");

                if (module.Description is not null)
                    sb.AppendLine($"    Description: {module.Description}");
            }

            sb.AppendLine();
        }

        // Remote boundaries
        if (_model.HasRemoteBoundaries)
        {
            sb.AppendLine($"Remote Boundaries ({_model.RemoteBoundaries.Length}):");
            sb.AppendLine("----------------------------------------");

            foreach (var rb in _model.RemoteBoundaries.OrderBy(r => r.ModuleName))
            {
                var urlInfo = rb.BaseUrl is not null
                    ? $" → {rb.BaseUrl}"
                    : " → (from configuration)";
                sb.AppendLine($"  [REMOTE] {rb.ModuleName}{urlInfo}");
                if (rb.AssemblyName is not null)
                    sb.AppendLine($"    Assembly: {rb.AssemblyName}");
            }

            sb.AppendLine();
        }

        // Assembly metadata summary
        sb.AppendLine($"Assembly Metadata ({_model.Assemblies.Length}):");
        foreach (var assembly in _model.Assemblies.OrderBy(a => a.AssemblyName))
        {
            sb.AppendLine($"  + {assembly.AssemblyName}");

            // Count entries by category
            var categories = assembly.Entries
                .GroupBy(e => GetCategoryName(e.Category))
                .OrderBy(g => g.Key);

            foreach (var category in categories)
                sb.AppendLine($"    - {category.Key}: {category.Count()} registration(s)");
        }

        sb.AppendLine();

        // Registration methods
        sb.AppendLine("Registration Methods:");
        var methods = _model.Assemblies
            .SelectMany(a => a.Entries)
            .Where(e => !string.IsNullOrEmpty(e.RegistrationMethod))
            .Select(e => e.RegistrationMethod)
            .Distinct()
            .OrderBy(m => m);

        var index = 1;
        foreach (var method in methods)
        {
            sb.AppendLine($"  {index}. {method}()");
            index++;
        }

        sb.AppendLine();

        // Validation errors (if any)
        if (_model.ValidationErrors.Length > 0)
        {
            sb.AppendLine("Validation Issues:");
            foreach (var error in _model.ValidationErrors)
            {
                var severity = Diagnostics.CompositionDiagnostics
                    .SchemaSeverity(error.DiagnosticId).ToString().ToUpperInvariant();
                var message = Diagnostics.CompositionDiagnostics
                    .Render(error.DiagnosticId, error.MessageArgs.AsImmutableArray());
                sb.AppendLine($"  [{severity}] {error.DiagnosticId}: {message}");
            }

            sb.AppendLine();
        }

        // Schema versions
        sb.AppendLine("Schema Versions:");
        var schemas = _model.Assemblies
            .SelectMany(a => a.Entries.Select(e => new { a.AssemblyName, e.Category, e.SchemaVersion }))
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key);

        foreach (var schema in schemas)
        {
            var versions = schema.Select(s => s.SchemaVersion).Distinct().ToList();
            var categoryName = GetCategoryName(schema.Key);
            sb.AppendLine($"  {categoryName}: {string.Join(", ", versions.Select(v => $"v{v}"))}");
        }

        return sb.ToString();
    }

    private static List<DiscoveredModuleInfo> TopologicalSort(
        ImmutableArray<DiscoveredModuleInfo> modules)
    {
        var result = new List<DiscoveredModuleInfo>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        var moduleMap = modules.ToDictionary(m => m.Name);

        void Visit(string name)
        {
            if (visited.Contains(name))
                return;
            if (visiting.Contains(name))
                return; // Circular dependency

            visiting.Add(name);

            if (moduleMap.TryGetValue(name, out var module))
            {
                foreach (var dep in module.DependsOn)
                    Visit(dep);
                result.Add(module);
            }

            visiting.Remove(name);
            visited.Add(name);
        }

        foreach (var module in modules)
            Visit(module.Name);

        return result;
    }

    /// <summary>Ordinal → member name, read off <see cref="MetadataCategoryIds" /> once.</summary>
    private static readonly Dictionary<string, string> CategoryNames =
        typeof(MetadataCategoryIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => (string)f.GetRawConstantValue(), f => f.Name);

    /// <summary>
    ///     The readable name of a category ordinal, or the ordinal itself when it names nothing.
    /// </summary>
    /// <remarks>
    ///     Not a hand-written switch: that would be a third copy of a correspondence already written
    ///     twice, and every category added after it would print as a bare number. Reading it off
    ///     <see cref="MetadataCategoryIds" /> is not a tidier way to keep the same list: it removes the
    ///     list, which is the only thing that stops it going stale.
    /// </remarks>
    private static string GetCategoryName(string category)
        => CategoryNames.TryGetValue(category, out var name) ? name : category;
}
