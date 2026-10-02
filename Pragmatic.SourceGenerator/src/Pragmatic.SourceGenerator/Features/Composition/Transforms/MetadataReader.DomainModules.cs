// Pragmatic.SourceGenerator - Composition - Metadata Reader (Domain Modules)

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Transforms;

/// <summary>
///     Domain module metadata reading methods for MetadataReader.
/// </summary>
internal static partial class MetadataReader
{
    /// <summary>
    ///     Reads [PragmaticModuleMetadata] attributes from referenced assemblies (Domain modules).
    /// </summary>
    public static ImmutableArray<DiscoveredModuleInfo> ReadDomainModulesFromReferences(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<DiscoveredModuleInfo>();
        var metadataAttribute = compilation.GetTypeByMetadataName(DomainModuleMetadataAttributeFullName);

        if (metadataAttribute is null)
            return results.ToImmutable();

        // Scan all referenced assemblies
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            foreach (var module in ReadDomainModulesFromAssembly(assembly, metadataAttribute, cancellationToken))
                results.Add(module);
        }

        return results.ToImmutable();
    }

    /// <summary>
    ///     Every <c>[PragmaticModuleMetadata]</c> on the assembly, one per boundary it declares.
    /// </summary>
    /// <remarks>
    ///     The attribute is <c>AllowMultiple</c> and the generator writes one per <c>[Boundary]</c>, so
    ///     an assembly with two boundaries carries two. Returning at the first would leave everything
    ///     else about the second boundary generated — its interface, its DbContext, its own metadata —
    ///     and none of it registered by the host, with nothing to read that said so. Which of the two
    ///     survived would be attribute order, that no author chooses.
    /// </remarks>
    private static IEnumerable<DiscoveredModuleInfo> ReadDomainModulesFromAssembly(
        IAssemblySymbol assembly,
        INamedTypeSymbol metadataAttribute,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, metadataAttribute))
                continue;

            var module = ParseDomainModuleAttribute(assembly.Name, attribute);
            if (module is not null)
                yield return module;
        }
    }

    private static DiscoveredModuleInfo? ParseDomainModuleAttribute(string assemblyName, AttributeData attribute)
    {
        string? boundaryTypeName = null;
        var readAccessTypes = ImmutableArray<string>.Empty;

        foreach (var namedArg in attribute.NamedArguments)
            switch (namedArg.Key)
            {
                case "BoundaryType":
                    if (namedArg.Value.Value is INamedTypeSymbol boundaryType)
                        boundaryTypeName = boundaryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    break;

                case "ReadAccessTypes":
                    if (!namedArg.Value.Values.IsDefaultOrEmpty)
                    {
                        var readAccess = ImmutableArray.CreateBuilder<string>();
                        foreach (var item in namedArg.Value.Values)
                            if (item.Value is INamedTypeSymbol readAccessType)
                                readAccess.Add(readAccessType.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat));

                        readAccessTypes = readAccess.ToImmutable();
                    }

                    break;
            }

        if (string.IsNullOrEmpty(boundaryTypeName))
            return null;

        return CreateDomainModule(assemblyName, boundaryTypeName, readAccessTypes);
    }

    /// <summary>
    ///     Builds the <see cref="DiscoveredModuleInfo"/> for one <c>[Boundary]</c>, from the same inputs
    ///     <c>BoundaryModuleMetadataTemplate</c> writes into <c>[PragmaticModuleMetadata]</c>.
    /// </summary>
    /// <remarks>
    ///     Shared with the host-local channel on purpose. A host that declares its own boundary cannot
    ///     read that attribute back — this generator run is what emits it — so the boundary model is
    ///     handed over directly instead. Deriving it twice would be two module names to keep in step, and
    ///     the name is what the generated <c>Add{Name}Boundary</c> call is built from.
    /// </remarks>
    public static DiscoveredModuleInfo CreateDomainModule(
        string assemblyName,
        string? boundaryTypeName,
        ImmutableArray<string> readAccessTypes = default)
    {
        // The module name is the boundary type's, less "Boundary" — the derivation the boundary's own
        // generated registration is named after. Strip "global::" first so LastIndexOf('.') finds only
        // namespace dots.
        var cleanBoundaryTypeName = (boundaryTypeName ?? string.Empty).Replace("global::", string.Empty);
        var lastDot = cleanBoundaryTypeName.LastIndexOf('.');
        var simpleTypeName = lastDot >= 0 ? cleanBoundaryTypeName.Substring(lastDot + 1) : cleanBoundaryTypeName;
        var moduleName = simpleTypeName.EndsWith("Boundary", StringComparison.Ordinal)
            ? simpleTypeName.Substring(0, simpleTypeName.Length - 8)
            : simpleTypeName;

        // DbContext registration — convention-based, present when Persistence.EFCore.SG runs.
        var dbContextMethod = $"Add{moduleName}DbContext";

        // Boundary DI, actions DI, mutations DI, and repositories DI are now generated
        // directly by the host from enriched metadata and convention-derived type names.

        return new DiscoveredModuleInfo
        {
            Name = moduleName,
            AssemblyName = assemblyName,
            BoundaryTypeName = boundaryTypeName,
            ReadAccessTypes = readAccessTypes.IsDefault ? ImmutableArray<string>.Empty : readAccessTypes,
            DbContextRegistrationMethod = dbContextMethod
        };
    }

    private static string ExtractNamespaceFromTypeName(string fullTypeName)
    {
        // Handle global:: prefix
        var typeName = fullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring(8)
            : fullTypeName;

        var lastDot = typeName.LastIndexOf('.');
        return lastDot > 0 ? typeName.Substring(0, lastDot) : string.Empty;
    }
}
