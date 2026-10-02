using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

internal static partial class ActionTransform
{
    /// <summary>Checks for explicit [BelongsTo&lt;TBoundary&gt;] directly on the action class.</summary>
    /// <remarks>
    ///     One <c>BelongsTo</c> now, in <c>Pragmatic.Persistence.Entity</c>. There were two identical
    ///     marker attributes of the same simple name, one here and one there, so any file importing
    ///     both namespaces — an ordinary thing for an entity — got <c>CS0104</c> and had to qualify.
    ///     Persistence's survived: ten of the thirteen readers in this generator accepted only it, and
    ///     Actions already references Persistence, so nothing gained a dependency.
    /// </remarks>
    private static string? ParseDirectBelongsTo(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition;
            if (originalDef.Name != "BelongsToAttribute"
                || originalDef.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity")
                continue;

            if (attrClass.TypeArguments.Length > 0)
                return attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
    }

    /// <summary>
    ///     Whether the direct <c>[BelongsTo&lt;T&gt;]</c> names a <b>package</b> rather than a boundary.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A package's actions declare their package this way. Read as "belongs to a boundary", the
    ///         invoker would take an <c>IUnitOfWork</c> keyed by the <em>package</em> type, which nothing
    ///         registers, so importing the package would stop the host from starting with a container
    ///         error naming a generated invoker. A package has no unit of work — its actions persist
    ///         through the store they hold — and this is what tells the two apart.
    ///     </para>
    ///     <para>
    ///         ⚠️ Asked of <c>IPackageDefinition</c> and not of <c>IBoundary</c>, deliberately: the
    ///         boundary interface is <b>added by this generator</b>, so a boundary declared in the
    ///         compilation being analysed does not implement it yet and the answer would be wrong for
    ///         every module's own boundary. <c>IPackageDefinition</c> is written by hand.
    ///     </para>
    /// </remarks>
    private static bool DirectBelongsToIsPackage(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition;
            if (originalDef.Name != "BelongsToAttribute"
                || originalDef.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity")
                continue;

            if (attrClass.TypeArguments.Length == 0)
                continue;

            return attrClass.TypeArguments[0] is INamedTypeSymbol target
                   && target.AllInterfaces.Any(i =>
                       i.Name == "IPackageDefinition"
                       && i.ContainingNamespace?.ToDisplayString() == "Pragmatic.Composition");
        }

        return false;
    }

    /// <summary>Infers BelongsTo from entity types injected via IRepository fields.</summary>
    private static string? ParseBelongsToFromEntity(INamedTypeSymbol symbol)
    {
        foreach (var member in symbol.GetMembers().OfType<IFieldSymbol>())
        {
            if (member.IsImplicitlyDeclared)
                continue;
            if (member.Type is not INamedTypeSymbol fieldType)
                continue;

            var originalDef = fieldType.OriginalDefinition;
            if (originalDef.Name != "IRepository")
                continue;
            var ns = originalDef.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            if (!ns.StartsWith("Pragmatic.Persistence"))
                continue;

            if (fieldType.TypeArguments.Length < 1)
                continue;
            if (fieldType.TypeArguments[0] is not INamedTypeSymbol entityType)
                continue;

            var boundary = GetBelongsToFromType(entityType);
            if (boundary is not null)
                return boundary;
        }

        return null;
    }

    /// <summary>The boundary an entity belongs to, seen from a symbol.</summary>
    /// <remarks>
    ///     The declared <c>[BelongsTo]</c> first, because it is the override and wins. Otherwise the
    ///     boundary that owns the entity, which — for an entity that names none — is the single
    ///     <c>[Boundary]</c> of its assembly. Reading only the attribute is not enough: an entity need
    ///     not carry it, so an action whose boundary comes from its repository field would have none,
    ///     and <c>PRAG0432</c> would say so about a perfectly ordinary action.
    /// </remarks>
    private static string? GetBelongsToFromType(INamedTypeSymbol typeSymbol)
        => BoundaryOwnershipReader.QualifiedBoundaryOf(typeSymbol);
}
