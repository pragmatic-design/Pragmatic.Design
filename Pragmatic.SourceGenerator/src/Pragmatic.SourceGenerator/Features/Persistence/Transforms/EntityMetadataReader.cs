using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads entity metadata from referenced assemblies by scanning their types for [Entity] attributes.
/// </summary>
/// <remarks>
///     <para>
///         This is a partial class split across multiple files for maintainability:
///         <list type="bullet">
///             <item>
///                 <description>EntityMetadataReader.cs - Core reading methods (this file)</description>
///             </item>
///             <item>
///                 <description>EntityMetadataReader.ReadAccess.cs - ReadAccess types per boundary</description>
///             </item>
///             <item>
///                 <description>EntityMetadataReader.Parsing.cs - Legacy parsing helpers (unused)</description>
///             </item>
///             <item>
///                 <description>EntityMetadataReader.Helpers.cs - Conversion helpers and enums</description>
///             </item>
///         </list>
///     </para>
/// </remarks>
internal static partial class EntityMetadataReader
{
    /// <summary>
    ///     Reads entity metadata from all referenced assemblies by scanning their types for [Entity] attributes.
    ///     Only active in host mode (ConsoleApplication / WindowsApplication) — returns empty for library projects.
    /// </summary>
    public static ImmutableArray<EntityMetadataModel> ReadFromReferences(
        Compilation compilation,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Only scan references in host mode (Exe = host, DLL = library module)
        if (compilation.Options.OutputKind is not (OutputKind.ConsoleApplication or OutputKind.WindowsApplication))
            return ImmutableArray<EntityMetadataModel>.Empty;

        var entities = ImmutableArray.CreateBuilder<EntityMetadataModel>();

        // Which boundary adopts the entities of which package, read from the modules that import them.
        var boundaryByPackageAssembly = ReadPackageAdoptions(compilation, ct);

        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;

            // Skip framework / runtime assemblies to avoid expensive traversal
            if (IsFrameworkAssembly(assembly.Name))
                continue;

            boundaryByPackageAssembly.TryGetValue(assembly.Name, out var adoptingBoundary);

            foreach (var type in GetAllNamedTypes(assembly.GlobalNamespace, ct))
            {
                ct.ThrowIfCancellationRequested();

                var model = EntityTransform.TransformFromSymbol(type, ct);
                if (model is null)
                    continue;

                entities.Add(Adopted(model with { IsFromReference = true }, adoptingBoundary));
            }
        }

        return entities.ToImmutable();
    }

    /// <summary>
    ///     Reads <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c> from every module the host can see, and
    ///     returns which boundary adopts each package assembly's entities.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Across references, not only in this compilation: the module that imports a package is
    ///         a library — <c>Showcase.Accounts</c> — while the compilation that builds the DbContexts
    ///         and the schema is the host. Only the host sees both the module's declaration and the
    ///         package's entities, which is why the composition happens here.
    ///     </para>
    ///     <para>
    ///         The one-argument form names no boundary and adopts nothing: a package whose entities
    ///         nobody claims stays unmapped, which is the honest answer rather than a guess.
    ///     </para>
    /// </remarks>
    internal static Dictionary<string, string> ReadPackageAdoptions(Compilation compilation, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var assembly in ModuleBearingAssemblies(compilation, ct))
        {
            foreach (var type in GetAllNamedTypes(assembly.GlobalNamespace, ct))
            {
                ct.ThrowIfCancellationRequested();

                var attributes = type.GetAttributes();
                if (!attributes.Any(a =>
                        a.AttributeClass?.Name == "ModuleAttribute" &&
                        a.AttributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Composition.Attributes"))
                    continue;

                foreach (var attribute in attributes)
                {
                    var attrClass = attribute.AttributeClass;
                    if (attrClass is null || !attrClass.IsGenericType) continue;
                    if (attrClass.Name != "UsePackageAttribute" ||
                        attrClass.ContainingNamespace?.ToDisplayString() != "Pragmatic.Composition.Attributes")
                        continue;
                    if (attrClass.TypeArguments.Length < 2) continue;

                    if (attrClass.TypeArguments[0] is not INamedTypeSymbol package ||
                        attrClass.TypeArguments[1] is not INamedTypeSymbol boundary)
                        continue;
                    if (package.ContainingAssembly?.Name is not { } packageAssembly)
                        continue;

                    result[packageAssembly] = boundary.ToDisplayString();
                }
            }
        }

        return result;
    }

    /// <summary>The current assembly and every non-framework reference, where a module may be declared.</summary>
    private static IEnumerable<IAssemblySymbol> ModuleBearingAssemblies(Compilation compilation, CancellationToken ct)
    {
        yield return compilation.Assembly;

        foreach (var reference in compilation.References)
        {
            ct.ThrowIfCancellationRequested();

            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly)
                continue;
            if (IsFrameworkAssembly(assembly.Name))
                continue;

            yield return assembly;
        }
    }

    /// <summary>The same entity, belonging to the boundary that adopted its package.</summary>
    internal static EntityMetadataModel Adopted(EntityMetadataModel entity, string? adoptingBoundary)
    {
        if (adoptingBoundary is null || !string.IsNullOrEmpty(entity.BoundaryTypeFullName))
            return entity;

        return entity with
        {
            BoundaryName = Core.BoundaryOwnershipReader.StripBoundarySuffix(SimpleName(adoptingBoundary)),
            BoundaryTypeFullName = adoptingBoundary,
            IsFromImportedPackage = true
        };
    }

    private static string SimpleName(string fullTypeName)
    {
        var name = fullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring(8)
            : fullTypeName;
        var lastDot = name.LastIndexOf('.');
        return lastDot < 0 ? name : name.Substring(lastDot + 1);
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol ns, CancellationToken ct)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            ct.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in type.GetTypeMembers())
                yield return nested;
        }

        foreach (var nestedNs in ns.GetNamespaceMembers())
        {
            ct.ThrowIfCancellationRequested();
            foreach (var type in GetAllNamedTypes(nestedNs, ct))
                yield return type;
        }
    }

    private static bool IsFrameworkAssembly(string name) =>
        name.StartsWith("System", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);
}
