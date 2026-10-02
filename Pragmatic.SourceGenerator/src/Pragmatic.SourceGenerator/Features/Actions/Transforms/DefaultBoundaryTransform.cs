using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The boundary a module gets when it declares none: <c>{Module}Boundary</c>, owning everything the
///     assembly holds.
/// </summary>
/// <remarks>
///     <para>
///         <b>Synthesised, not discovered.</b> The obvious shape — emit a class carrying
///         <c>[Boundary]</c> and let the boundary pipeline pick it up — cannot work: a generator does not
///         see its own output in the compilation it is generating for, so
///         <c>ForAttributeWithMetadataName</c> would find nothing and the type would sit there with no
///         interface, no DbContext and no metadata hanging off it. The model is built here from the
///         <c>[Module]</c> symbol instead, and the class is emitted alongside everything derived from it,
///         in the same pass.
///     </para>
///     <para>
///         Only for a library. A host is an executable with an entry point and declares <c>[Module]</c>
///         too — giving it a boundary would invent an actions interface and a DbContext for the
///         composition root.
///     </para>
/// </remarks>
internal static class DefaultBoundaryTransform
{
    private const string ModuleSuffix = "Module";
    private const string BoundarySuffix = "Boundary";

    /// <summary>
    ///     Builds the default boundary for <paramref name="moduleSymbol" />, or <see langword="null" />
    ///     when the module has no namespace to put it in.
    /// </summary>
    public static BoundaryModel? FromModule(INamedTypeSymbol moduleSymbol)
    {
        var ns = moduleSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : moduleSymbol.ContainingNamespace.ToDisplayString();

        if (string.IsNullOrEmpty(ns))
            return null;

        var shortName = moduleSymbol.Name.EndsWith(ModuleSuffix, StringComparison.Ordinal)
            ? moduleSymbol.Name.Substring(0, moduleSymbol.Name.Length - ModuleSuffix.Length)
            : moduleSymbol.Name;

        if (shortName.Length == 0)
            return null;

        var typeName = shortName + BoundarySuffix;

        return new BoundaryModel
        {
            Namespace = ns,
            TypeName = typeName,
            FullTypeName = $"global::{ns}.{typeName}",
            InterfaceName = $"I{shortName}Actions",
            InternalInterfaceName = $"I{shortName}InternalActions",
            ImplementationName = $"{shortName}LocalActions",
            Accessibility = "public",
            InvalidReason = BoundaryInvalidReason.None,
            LocationInfo = null,
            IsGenerated = true
        };
    }
}
