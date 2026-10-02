// Pragmatic.Composition.SourceGenerator - Database Topology Validator

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Validates database topology consistency for host-level [Include&lt;T&gt;] declarations.
///     Reports:
///     - PRAG1651: Boundary included without database assignment.
///     - PRAG1652: Same DbContext class name assigned to two different databases.
/// </summary>
internal static class DatabaseTopologyValidator
{
    /// <summary>
    ///     Validates the host include declarations and reports diagnostics.
    /// </summary>
    /// <param name="context">Source production context for diagnostic reporting.</param>
    /// <param name="includes">All [Include&lt;T&gt;] declarations from the host module.</param>
    /// <param name="domainModules">Domain modules discovered from referenced assemblies.</param>
    public static void Validate(
        SourceProductionContext context,
        ImmutableArray<HostIncludeModel> includes,
        ImmutableArray<DiscoveredModuleInfo> domainModules)
    {
        if (includes.IsDefaultOrEmpty)
            return;

        ValidateBoundaryDatabaseAssignments(context, includes, domainModules);
        ValidateDbContextNameCollisions(context, includes);
        ValidateModuleNameResolution(context, includes, domainModules);
        ValidateDatabaseConfigKeys(context, includes);
    }

    /// <summary>
    ///     PRAG1609 (DB-CONFIGKEY-MISSING): a relational database with no ConfigKey generates a DbContext
    ///     without a connection string (the template emits a placeholder) → runtime startup failure.
    ///     Mirrors the template's placeholder condition (non-InMemory provider + empty config key).
    /// </summary>
    private static void ValidateDatabaseConfigKeys(
        SourceProductionContext context,
        ImmutableArray<HostIncludeModel> includes)
    {
        var reported = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        foreach (var include in includes)
        {
            if (!include.HasDatabase)
                continue;

            var provider = include.DatabaseProvider;
            // Only relational providers need a connection string; InMemory / unspecified do not.
            if (provider is null || string.Equals(provider, "InMemory", System.StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrEmpty(include.DatabaseConfigKey))
                continue;

            var dbName = GetSimpleName(include.DatabaseTypeName ?? "database");
            if (reported.Add(dbName))
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.DatabaseConfigKeyMissing,
                    include.Location ?? Location.None, dbName, provider));
        }
    }

    /// <summary>
    ///     PRAG1607 / PRAG1608: mirrors the host template's 2-arity DB binding (module simple name →
    ///     discovered module with a DbContext registration method). Surfaces the two silent failures:
    ///     duplicate module names (ambiguous binding, TOPO-DUPNAME) and a module whose metadata was never
    ///     discovered (its DbContext is silently not registered, TOPO-DBBIND-SILENT).
    /// </summary>
    private static void ValidateModuleNameResolution(
        SourceProductionContext context,
        ImmutableArray<HostIncludeModel> includes,
        ImmutableArray<DiscoveredModuleInfo> domainModules)
    {
        var dbBound = domainModules
            .Where(m => m.DbContextRegistrationMethod is not null)
            .ToList();

        // PRAG1607 — duplicate names make the template's ToDictionary(m => m.Name) ambiguous (it also threw).
        var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        var reported = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
        foreach (var m in dbBound)
            if (!seen.Add(m.Name) && reported.Add(m.Name))
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.DuplicateModuleName, Location.None, m.Name));

        var byName = new System.Collections.Generic.HashSet<string>(
            dbBound.Select(m => m.Name), System.StringComparer.Ordinal);

        // The assembly is what an include really names, and it is what the host template binds on.
        // ⚠️ Not by name: matching the module's simple name against the discovered module's name — which
        // is derived from its BOUNDARY — held only while every module had one boundary named after it.
        // A module with two boundaries has no name that matches either, so this reported "not
        // discovered" for a module whose metadata was right there, twice.
        var byAssembly = new System.Collections.Generic.HashSet<string>(
            dbBound.Select(m => m.AssemblyName), System.StringComparer.Ordinal);

        // PRAG1608 — a 2-arity include (DB, no explicit DbContext) whose module isn't discovered.
        foreach (var include in includes)
        {
            if (!include.HasDatabase || include.HasExplicitDbContext)
                continue;

            if (include.ModuleAssemblyName is { Length: > 0 } assembly && byAssembly.Contains(assembly))
                continue;

            // Name as the fallback, for an include whose module symbol did not resolve: there the
            // assembly is unknown and the name reading is the only one available.
            if (!byName.Contains(GetModuleSimpleName(include.ModuleTypeName)))
                context.ReportDiagnostic(Diagnostic.Create(
                    CompositionDiagnostics.IncludeModuleNotDiscovered,
                    include.Location ?? Location.None,
                    GetSimpleName(include.ModuleTypeName)));
        }
    }

    private static string GetModuleSimpleName(string fullTypeName)
    {
        var simple = GetSimpleName(fullTypeName);
        return simple.EndsWith("Module", System.StringComparison.Ordinal)
            ? simple.Substring(0, simple.Length - 6)
            : simple;
    }

    /// <summary>
    ///     PRAG1651: Warns for each [Include&lt;TModule&gt;] (1-arity) that has no database.
    /// </summary>
    private static void ValidateBoundaryDatabaseAssignments(
        SourceProductionContext context,
        ImmutableArray<HostIncludeModel> includes,
        ImmutableArray<DiscoveredModuleInfo> domainModules)
    {
        // Build a lookup: moduleTypeName → domainModule name
        var moduleNames = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal);

        foreach (var dm in domainModules)
            if (dm.BoundaryTypeName is not null)
                moduleNames[dm.BoundaryTypeName] = dm.Name;

        foreach (var include in includes)
        {
            if (include.HasDatabase)
                continue;

            // 1-arity include has no database — emit warning
            var moduleName = moduleNames.TryGetValue(include.ModuleTypeName, out var n)
                ? n
                : GetSimpleName(include.ModuleTypeName);

            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.BoundaryWithoutDatabase,
                include.Location ?? Location.None,
                moduleName,
                include.ModuleTypeName));
        }
    }

    /// <summary>
    ///     PRAG1652: Errors when the same DbContext class name maps to different databases.
    /// </summary>
    private static void ValidateDbContextNameCollisions(
        SourceProductionContext context,
        ImmutableArray<HostIncludeModel> includes)
    {
        // Map DbContextClassName → first DatabaseTypeName seen
        var dbContextToDatabase = new System.Collections.Generic.Dictionary<string, string>(
            System.StringComparer.Ordinal);

        foreach (var include in includes)
        {
            if (include.DbContextClassName is null || include.DatabaseTypeName is null)
                continue;

            if (dbContextToDatabase.TryGetValue(include.DbContextClassName, out var existingDb))
            {
                if (!string.Equals(existingDb, include.DatabaseTypeName, System.StringComparison.Ordinal))
                    context.ReportDiagnostic(Diagnostic.Create(
                        CompositionDiagnostics.DbContextNameCollision,
                        include.Location ?? Location.None,
                        include.DbContextClassName,
                        existingDb,
                        include.DatabaseTypeName));
            }
            else
            {
                dbContextToDatabase[include.DbContextClassName] = include.DatabaseTypeName;
            }
        }
    }

    private static string GetSimpleName(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        var name = lastDot >= 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;
        // Strip global:: prefix
        if (name.StartsWith("global::", System.StringComparison.Ordinal))
            name = name.Substring(8);
        return name;
    }
}
