using System.Linq;
using Pragmatic.SourceGenerator.Features.Mapping.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Which children a patch carries, and whether it can write them.
/// </summary>
/// <remarks>
///     These were filtered out of <c>ApplyPatch</c> entirely, so a patch with a <c>Lines</c> collection
///     wrote none of it and said nothing about doing so. The code that turns one into an assignment
///     lives in <see cref="Mapping.Templates.ChildWritingTemplate" />, shared with the mutation side;
///     what is here is only which properties qualify.
/// </remarks>
internal sealed partial class PatchApplyTemplate
{
    /// <summary>
    ///     Whether the property carries a child entity rather than a value.
    /// </summary>
    /// <remarks>
    ///     A collection of <c>string</c> is not a child: it has no identity to preserve, and it was
    ///     never in scope here. What counts is a DTO the developer wrote for an entity of their own.
    /// </remarks>
    internal static bool IsRelated(MutationPropertyModel prop)
    {
        return prop.RelatedIsUserType && (prop.IsCollection || prop.IsNestedMutation || prop.RelatedCanCreate);
    }

    /// <summary>
    ///     Whether the patch can write this child at all.
    /// </summary>
    /// <remarks>
    ///     A collection needs both a way to build or update an element and something to match by; a
    ///     nested DTO needs only the former, because there is exactly one child and no matching to do.
    /// </remarks>
    internal static bool CanWrite(MutationPropertyModel prop)
    {
        if (!prop.RelatedCanCreate && !prop.RelatedCanPatch)
            return false;

        return !prop.IsCollection || prop.CollectionWrite is { KeyProblem: CollectionKeyProblem.None };
    }

    /// <summary>
    ///     The navigation paths this patch writes into, deep and prefixed.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A patch merges: it decides what to keep by looking at what is already on the entity, so
    ///         a child collection nobody loaded is a collection it writes again. The caller of
    ///         <c>ApplyPatch</c> is hand-written code holding its own entity — nothing loads for it —
    ///         so the list it needs has to be readable from the type itself.
    ///     </para>
    ///     <para>
    ///         Emitted on every patch, empty list included, for the same reason as its two twins on a
    ///         mutation and on a <c>[MapTo]</c> DTO: a caller names it without being able to see
    ///         whether this model had anything to put in it.
    ///     </para>
    ///     <para>
    ///         The deeper levels are composed at <b>runtime</b> from each child's own list. This
    ///         transform sees one patch; what its children write belongs to their models.
    ///     </para>
    /// </remarks>
    private void RenderWrittenNavigations()
    {
        var written = _model.Properties
            .Where(prop => !prop.IsIgnored && IsRelated(prop) && CanWrite(prop))
            .Where(prop => prop.CollectionWrite is not { Strategy: "Ignore" })
            .ToList();

        AppendLine();
        XmlSummary(
            "Navigation paths this patch writes into. Load them before applying it: a merge decides "
            + "what to keep by looking at what is there, so an unloaded one is written again.");

        if (written.Count == 0)
        {
            AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> "
                + "WrittenNavigations { get; } = [];");
            return;
        }

        AppendLine("public static global::System.Collections.Generic.IReadOnlyList<string> "
            + "WrittenNavigations { get; } =");
        AppendLine("[");
        IncreaseIndent();

        foreach (var prop in written)
        {
            AppendLine($"\"{prop.TargetPropertyName}\",");

            if (prop.RelatedDtoFullTypeName is { Length: > 0 } childType)
                AppendLine($".. global::System.Linq.Enumerable.Select({childType}"
                    + $".WrittenNavigations, __p => \"{prop.TargetPropertyName}.\" + __p),");
        }

        DecreaseIndent();
        AppendLine("];");
    }

    private void RenderRelatedWrite(MutationPropertyModel prop)
    {
        RenderChildWrite(
            new ChildWriteModel
            {
                PropertyName = prop.PropertyName,
                TargetPropertyName = prop.TargetPropertyName,
                IsCollection = prop.IsCollection,
                Collection = prop.CollectionWrite,
                CanCreate = prop.RelatedCanCreate,
                CanPatch = prop.RelatedCanPatch,
                EntityHasPrivateSetter = prop.EntityHasPrivateSetter,
                ReferenceStrategy = prop.ReferenceStrategy,
            },
            "target");
    }
}
