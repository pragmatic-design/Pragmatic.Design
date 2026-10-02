using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

internal static partial class EntityMetadataReader
{
    private const string ModuleMetadataAttributeName = "Pragmatic.Actions.Metadata.PragmaticModuleMetadataAttribute";

    /// <summary>
    ///     Reads ReadAccess entity types per boundary from <c>PragmaticModuleMetadataAttribute</c>
    ///     in all referenced assemblies and the current compilation's assembly.
    /// </summary>
    /// <returns>
    ///     A mapping of boundary type full name (e.g., "Contoso.Catalog.CatalogBoundary")
    ///     to the array of ReadAccess entity type full names.
    /// </returns>
    /// <remarks>
    ///     Value-equatable on purpose: this read is driven by the <c>CompilationProvider</c>, so it runs
    ///     again on every edit. A raw <c>ImmutableDictionary</c> of raw <c>ImmutableArray</c>s compares by
    ///     reference and would mark the step Modified every time, re-running DbContext and schema-metadata
    ///     generation for a change that touched neither.
    /// </remarks>
    public static EquatableDictionary<string, EquatableArray<string>> ReadReadAccessTypesByBoundary(
        Compilation compilation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var moduleMetadataType = compilation.GetTypeByMetadataName(ModuleMetadataAttributeName);
        if (moduleMetadataType is null)
            return EquatableDictionary<string, EquatableArray<string>>.Empty;

        var result = new Dictionary<string, EquatableArray<string>>();

        // Read from referenced assemblies
        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            ReadModuleMetadataFromAssembly(assembly, moduleMetadataType, result, ct);
        }

        // Also read from current compilation's assembly
        ReadModuleMetadataFromAssembly(compilation.Assembly, moduleMetadataType, result, ct);

        return result.ToImmutableDictionary();
    }

    private static void ReadModuleMetadataFromAssembly(
        IAssemblySymbol assembly,
        INamedTypeSymbol moduleMetadataType,
        Dictionary<string, EquatableArray<string>> result,
        CancellationToken ct)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            ct.ThrowIfCancellationRequested();

            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, moduleMetadataType))
                continue;

            string? boundaryTypeName = null;
            var readAccessTypeNames = ImmutableArray.CreateBuilder<string>();

            foreach (var namedArg in attribute.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "BoundaryType":
                        if (namedArg.Value.Value is INamedTypeSymbol boundaryType)
                            boundaryTypeName = boundaryType.ToDisplayString();
                        break;

                    case "ReadAccessTypes":
                        if (namedArg.Value.Kind == TypedConstantKind.Array)
                            foreach (var typeConst in namedArg.Value.Values)
                                if (typeConst.Value is INamedTypeSymbol entityType)
                                    readAccessTypeNames.Add(entityType.ToDisplayString());
                        break;
                }
            }

            if (boundaryTypeName is not null && readAccessTypeNames.Count > 0)
                result[boundaryTypeName] = readAccessTypeNames.ToImmutable();
        }
    }
}
