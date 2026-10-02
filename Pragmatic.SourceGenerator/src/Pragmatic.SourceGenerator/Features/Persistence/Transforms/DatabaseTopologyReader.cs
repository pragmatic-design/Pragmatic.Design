using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads [Include&lt;TModule, TDatabase&gt;] topology from the host [Module] class
///     and correlates modules with their boundary types via [PragmaticModuleMetadata].
///     This provides the boundary→database mapping needed for per-database MigrationDbContext generation.
/// </summary>
internal static class DatabaseTopologyReader
{
    private const string IncludeAttributePrefix = "Pragmatic.Composition.Attributes.IncludeAttribute";
    private const string PragmaticDatabaseAttributeName = "Pragmatic.Composition.Attributes.PragmaticDatabaseAttribute";
    private const string ModuleAttributeName = "Pragmatic.Composition.Attributes.ModuleAttribute";
    private const string DomainModuleMetadataAttributeName = "Pragmatic.Actions.Metadata.PragmaticModuleMetadataAttribute";

    /// <summary>
    ///     Reads database topology from the compilation.
    ///     Returns a mapping from boundary type FQN → database assignment.
    /// </summary>
    public static DatabaseTopologyInfo Read(Compilation compilation, CancellationToken ct)
    {
        // Step 1: Find [Module] class in current compilation and extract [Include<T,D>] declarations
        var moduleIncludes = ReadHostIncludes(compilation, ct);
        if (moduleIncludes.Length == 0)
            return new DatabaseTopologyInfo();

        // Step 2: Build assembly → database mapping from includes. Keyed by the assembly the module
        // type lives in, because that is what step 3 has in hand while reading a boundary.
        var moduleAssemblies = new Dictionary<string, DatabaseAssignment>(StringComparer.Ordinal);
        foreach (var include in moduleIncludes)
        {
            if (include.Assignment is not null && include.ModuleAssemblyName.Length > 0)
                moduleAssemblies[include.ModuleAssemblyName] = include.Assignment;
        }

        if (moduleAssemblies.Count == 0)
            return new DatabaseTopologyInfo();

        // Step 3: Read [PragmaticModuleMetadata] from referenced assemblies to get module→boundary mapping
        var boundaryToDatabaseBuilder = ImmutableDictionary.CreateBuilder<string, DatabaseAssignment>();
        var metadataAttr = compilation.GetTypeByMetadataName(DomainModuleMetadataAttributeName);

        if (metadataAttr is not null)
        {
            foreach (var reference in compilation.References)
            {
                ct.ThrowIfCancellationRequested();
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                    continue;

                foreach (var attr in assembly.GetAttributes())
                {
                    if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, metadataAttr))
                        continue;

                    // Extract BoundaryType from [PragmaticModuleMetadata]
                    string? boundaryTypeName = null;
                    foreach (var namedArg in attr.NamedArguments)
                    {
                        if (namedArg is { Key: "BoundaryType", Value.Value: INamedTypeSymbol boundaryType })
                        {
                            boundaryTypeName = boundaryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                            break;
                        }
                    }

                    if (boundaryTypeName is null)
                        continue;

                    // The boundary belongs to the module of the assembly declaring it. That is where
                    // the attribute is: the generator writes one per [Boundary] into the assembly that
                    // holds it, so "which module owns this boundary" is the assembly we are reading,
                    // not a name that happens to look like another name.
                    if (moduleAssemblies.TryGetValue(assembly.Name, out var assignment))
                        boundaryToDatabaseBuilder[boundaryTypeName] = assignment;
                }
            }
        }

        return new DatabaseTopologyInfo
        {
            BoundaryToDatabase = boundaryToDatabaseBuilder.ToImmutable()
        };
    }

    private sealed class IncludeEntry
    {
        public string ModuleTypeName { get; set; } = "";

        /// <summary>The assembly the module type lives in — the key the boundary is matched on.</summary>
        public string ModuleAssemblyName { get; set; } = "";

        public DatabaseAssignment? Assignment { get; set; }
    }

    /// <summary>
    ///     Reads [Include&lt;TModule, TDatabase&gt;] from [Module]-decorated classes in current compilation.
    /// </summary>
    private static ImmutableArray<IncludeEntry> ReadHostIncludes(
        Compilation compilation, CancellationToken ct)
    {
        var results = ImmutableArray.CreateBuilder<IncludeEntry>();
        var moduleAttr = compilation.GetTypeByMetadataName(ModuleAttributeName);
        if (moduleAttr is null)
            return results.ToImmutable();

        // Scan types in current compilation for [Module] attribute
        var visitor = new ModuleClassVisitor(moduleAttr, results, ct);
        visitor.Visit(compilation.Assembly.GlobalNamespace);

        return results.ToImmutable();
    }

    /// <summary>
    ///     Visits namespace members to find [Module]-decorated types.
    /// </summary>
    private sealed class ModuleClassVisitor : SymbolVisitor
    {
        private readonly INamedTypeSymbol _moduleAttr;
        private readonly ImmutableArray<IncludeEntry>.Builder _results;
        private readonly CancellationToken _ct;

        public ModuleClassVisitor(
            INamedTypeSymbol moduleAttr,
            ImmutableArray<IncludeEntry>.Builder results,
            CancellationToken ct)
        {
            _moduleAttr = moduleAttr;
            _results = results;
            _ct = ct;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                _ct.ThrowIfCancellationRequested();
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            // Check if this type has [Module] attribute
            var hasModule = false;
            foreach (var attr in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _moduleAttr))
                {
                    hasModule = true;
                    break;
                }
            }

            if (!hasModule)
                return;

            // Read [Include<T,D>] attributes
            foreach (var attr in symbol.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass is null || !attrClass.IsGenericType)
                    continue;

                var originalDef = attrClass.OriginalDefinition.ToDisplayString();
                if (!originalDef.StartsWith(IncludeAttributePrefix))
                    continue;

                var args = attrClass.TypeArguments;
                if (args.Length == 0)
                    continue;

                var moduleTypeName = args[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var moduleAssemblyName = args[0].ContainingAssembly?.Name ?? "";

                if (args.Length < 2)
                {
                    _results.Add(new IncludeEntry
                    { ModuleTypeName = moduleTypeName, ModuleAssemblyName = moduleAssemblyName });
                    continue;
                }

                var databaseType = args[1] as INamedTypeSymbol;
                if (databaseType is null)
                {
                    _results.Add(new IncludeEntry
                    { ModuleTypeName = moduleTypeName, ModuleAssemblyName = moduleAssemblyName });
                    continue;
                }

                var databaseTypeName = databaseType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                // Read ConfigKey from [PragmaticDatabase(ConfigKey = "...")] on the database class
                string? configKey = null;
                foreach (var dbAttr in databaseType.GetAttributes())
                {
                    if (dbAttr.AttributeClass?.Name != "PragmaticDatabaseAttribute") continue;
                    foreach (var named in dbAttr.NamedArguments)
                    {
                        if (named.Key == "ConfigKey")
                            configKey = named.Value.Value as string;
                    }
                    break;
                }

                _results.Add(new IncludeEntry
                {
                    ModuleTypeName = moduleTypeName,
                    ModuleAssemblyName = moduleAssemblyName,
                    Assignment = new DatabaseAssignment
                    {
                        DatabaseTypeName = databaseTypeName,
                        DatabaseClassName = databaseType.Name,
                        ConfigKey = configKey,
                        // The [Include<,>] site is what a topology diagnostic must point at; fall back to
                        // the [Module] class when the attribute has no syntax (never in practice — the
                        // visitor only walks the current compilation).
                        Location = LocationInfo.From(
                            attr.ApplicationSyntaxReference?.GetSyntax(_ct).GetLocation()
                            ?? symbol.Locations.FirstOrDefault())
                    }
                });
            }
        }
    }
}
