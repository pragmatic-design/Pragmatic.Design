using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;

namespace Pragmatic.SourceGenerator.Features.Actions;

/// <summary>
///     Package action registration: reads [UsePackage&lt;T&gt;] from modules and creates sub-boundary models.
/// </summary>
internal static partial class ActionsFeature
{
    // =========================================================================
    // Package sub-boundary creation
    // =========================================================================

    /// <summary>
    ///     Creates SubBoundaryModel entries from package action registrations.
    ///     Each package becomes a typed sub-boundary on the boundary interface.
    /// </summary>
    private static ImmutableArray<SubBoundaryModel> CreatePackageSubBoundaries(
        BoundaryModel boundary,
        ImmutableArray<PackageActionRegistration> pkgRegs)
    {
        if (pkgRegs.IsEmpty) return ImmutableArray<SubBoundaryModel>.Empty;

        var shortBoundaryName = StripBoundarySuffix(boundary.TypeName);
        var packageName = DerivePackageName(pkgRegs);
        var members = pkgRegs.Select(BoundaryMemberModel.FromPackageRegistration).ToImmutableArray();

        return ImmutableArray.Create(new SubBoundaryModel
        {
            Name = packageName,
            FullPath = packageName,
            InterfaceName = $"I{shortBoundaryName}{packageName}Actions",
            ImplementationName = $"{shortBoundaryName}{packageName}LocalActions",
            PropertyName = packageName,
            Description = $"Package actions from {packageName}",
            PublicMembers = members
        });
    }

    /// <summary>
    ///     Derives a short package name from the action types.
    ///     E.g., "global::Pragmatic.Identity.Local.Actions.LoginUser" → "Identity"
    /// </summary>
    private static string DerivePackageName(ImmutableArray<PackageActionRegistration> regs)
    {
        if (regs.IsEmpty) return "Package";

        var firstType = regs[0].ActionType;
        if (firstType.StartsWith("global::", StringComparison.Ordinal))
            firstType = firstType.Substring(8);

        // "Pragmatic.Identity.Local.Actions.LoginUser" → segments
        var segments = firstType.Split('.');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i] == "Pragmatic" && i + 1 < segments.Length && segments[i + 1] != "Actions")
                return segments[i + 1];
        }

        return segments.Length > 1 ? segments[1] : segments[0];
    }

    // =========================================================================
    // Package action registration reading
    // =========================================================================

    /// <summary>
    ///     Reads package action registrations from referenced assemblies for boundaries
    ///     whose modules have [UsePackage&lt;T&gt;].
    ///     Returns a dictionary: boundary namespace → package action registrations.
    /// </summary>
    private static Dictionary<string, PackageImport> ReadPackageRegistrations(
        Compilation compilation)
    {
        var result = new Dictionary<string, PackageImport>();

        var assemblyMetadata = MetadataReader.ReadFromReferences(compilation, default);

        // PERF: do NOT walk every syntax node of every tree (DescendantNodes() is O(all-nodes)
        // and forces a semantic model per tree). Module classes carrying [Module] + [UsePackage<T>]
        // are named types declared in the current assembly, so we enumerate type SYMBOLS directly
        // from the global namespace and inspect their attributes. This visits only declared types,
        // not statements/expressions, and skips the per-tree GetSemanticModel cost entirely.
        foreach (var type in EnumerateSourceNamedTypes(compilation.Assembly.GlobalNamespace))
        {
            // Cheap pre-filter: a module must carry [Module]. Bail before doing any package work.
            var hasModule = type.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "ModuleAttribute" &&
                a.AttributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Composition.Attributes");
            if (!hasModule) continue;

            var packageAssemblyNames = new List<string>();

            // The boundary named on the two-argument form, per package. A package declares none of its
            // own, so the key for DbContext/IUnitOfWork can only come from here: the invoker's
            // constructor was fixed in the package's compilation and cannot be keyed afterwards.
            // ⚠️ Per package and not per module: one import naming a boundary must not answer for
            // another that named none, or a package would be wired by a declaration about a different
            // package.
            var boundaryByPackageAssembly = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var attr in type.GetAttributes())
            {
                var attrClass = attr.AttributeClass;
                if (attrClass is null || !attrClass.IsGenericType) continue;
                if (attrClass.Name != "UsePackageAttribute" ||
                    attrClass.ContainingNamespace?.ToDisplayString() != "Pragmatic.Composition.Attributes")
                    continue;

                var typeArg = attrClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
                if (typeArg?.ContainingAssembly?.Name is null)
                    continue;

                var packageAssembly = typeArg.ContainingAssembly.Name;
                packageAssemblyNames.Add(packageAssembly);

                if (attrClass.TypeArguments.Length > 1 &&
                    attrClass.TypeArguments[1] is INamedTypeSymbol boundaryArg)
                {
                    boundaryByPackageAssembly[packageAssembly] =
                        boundaryArg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }

            if (packageAssemblyNames.Count == 0) continue;

            var moduleNs = type.ContainingNamespace?.ToDisplayString() ?? "";

            var packageMetadata = assemblyMetadata
                .Where(a => packageAssemblyNames.Contains(a.AssemblyName))
                .ToImmutableArray();

            if (packageMetadata.IsEmpty) continue;

            var (pkgActions, pkgMutations) = MetadataReader.ExtractActionRegistrations(packageMetadata);

            var registrations = ImmutableArray.CreateBuilder<PackageActionRegistration>();

            foreach (var action in pkgActions)
            {
                registrations.Add(new PackageActionRegistration
                {
                    ActionType = action.ActionType,
                    InvokerType = action.InvokerType,
                    IsVoid = action.IsVoid,
                    ReturnType = action.ReturnType,
                    BoundaryKeyedServices = action.BoundaryKeyedServices,
                    SourceAssembly = action.SourceAssembly
                });
            }

            foreach (var mutation in pkgMutations)
            {
                registrations.Add(new PackageActionRegistration
                {
                    ActionType = mutation.MutationType,
                    InvokerType = mutation.InvokerType,
                    IsMutation = true,
                    EntityType = mutation.EntityType,
                    MutationReturnKind = mutation.ReturnKind,
                    BoundaryKeyedServices = mutation.BoundaryKeyedServices,
                    SourceAssembly = mutation.SourceAssembly
                });
            }

            if (registrations.Count > 0)
            {
                result[moduleNs] = new PackageImport(
                    registrations.ToImmutable(),
                    boundaryByPackageAssembly,
                    Core.LocationInfo.From(type.Locations.FirstOrDefault()));
            }
        }

        return result;
    }

    /// <summary>
    ///     Enumerates named types declared in source (this compilation's assembly), recursing through
    ///     namespaces and nested types. Visits only type symbols — never statements/expressions — so it
    ///     is far cheaper than a full <c>DescendantNodes()</c> syntax walk and needs no semantic model.
    /// </summary>
    private static IEnumerable<INamedTypeSymbol> EnumerateSourceNamedTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol childNs:
                    foreach (var nested in EnumerateSourceNamedTypes(childNs))
                        yield return nested;
                    break;

                case INamedTypeSymbol type:
                    yield return type;
                    foreach (var nested in EnumerateNestedTypes(type))
                        yield return nested;
                    break;
            }
        }
    }

    /// <summary>
    ///     What one module's <c>[UsePackage]</c> declarations amount to: the operations they fuse in,
    ///     and the boundaries they name.
    /// </summary>
    /// <remarks>
    ///     The boundary is recorded per package, because a module may import several and each names
    ///     its own. Two imports naming <b>different</b> boundaries still cannot both be honoured — the
    ///     bridge is a single unkeyed registration in the container, so the second would shadow the
    ///     first. That is <c>PRAG0450</c>.
    /// </remarks>
    internal sealed record PackageImport(
        ImmutableArray<PackageActionRegistration> Registrations,
        Dictionary<string, string> BoundaryByPackageAssembly,
        Core.LocationInfo? Location);

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNestedTypes(nested))
                yield return deeper;
        }
    }
}
