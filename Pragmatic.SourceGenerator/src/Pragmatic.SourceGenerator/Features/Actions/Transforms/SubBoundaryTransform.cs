using System.Collections.Generic;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Infers sub-boundary grouping from namespace segments between the boundary prefix and the operation type segment.
/// </summary>
internal static class SubBoundaryTransform
{
    // Segments that represent operation types (not sub-boundary grouping). Infrastructure/ is the
    // folder for what the module uses and does not publish: the segment stays in the namespace —
    // namespace = folder, no exceptions — and is discarded here, so an operation filed under it lands
    // on the root instead of in a group named after the folder.
    private static readonly HashSet<string> OperationTypeSegments = new(StringComparer.Ordinal)
    {
        "Mutations", "Actions", "Queries", "Endpoints", "Entities", "Dtos",
        // Enums beside Entities and Dtos: all three name a kind of declaration rather than a resource,
        // and a folder of enums holds no operation — so the day one is filed there, the group would be
        // called "Enums" and nothing would say so.
        "Enums",
        "Events", "EventHandlers", "Errors", "Services", "Validators",
        "Specifications", "Filters", "Handlers", "Processors", "Converters",
        "Seeding", "Lifecycle", "FeatureFlags", "Infrastructure"
    };

    private const string SubBoundaryAttributeName = "Pragmatic.Actions.Attributes.SubBoundaryAttribute";

    /// <summary>
    ///     The group an operation <b>declares</b> with <c>[SubBoundary(Name = "…")]</c>, verbatim, or
    ///     null when it declares none.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Verbatim — whitespace and all — because the boundary pass has to tell "declared nothing"
    ///     from "declared an empty name", and only the second is PRAG0416. The field
    ///     the boundary builder prefers over the namespace is populated by generated operations —
    ///     <c>[Resource]</c> and the traits — and by this, the door for the attribute a developer
    ///     writes.
    /// </remarks>
    public static string? Declared(Microsoft.CodeAnalysis.ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != SubBoundaryAttributeName)
                continue;

            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "Name")
                    return argument.Value.Value as string ?? "";

            // Written with no Name: the author asked for a group and named none.
            return "";
        }

        return null;
    }

    /// <summary>The usable form of a declared name, or null when it is blank.</summary>
    public static string? Usable(string? declared)
        => string.IsNullOrWhiteSpace(declared) ? null : declared!.Trim();

    /// <summary>
    ///     What <c>[SubBoundary(Description = …)]</c> says: the summary of the generated group
    ///     interface, in the author's words.
    /// </summary>
    /// <remarks>
    ///     It reached no model and no template either — the second dead half beside <c>Name</c>, and
    ///     the issue that fixed one had to answer for the other rather than leave it.
    /// </remarks>
    public static string? DeclaredDescription(Microsoft.CodeAnalysis.ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != SubBoundaryAttributeName)
                continue;

            foreach (var argument in attribute.NamedArguments)
                if (argument.Key == "Description" && argument.Value.Value is string description
                    && !string.IsNullOrWhiteSpace(description))
                    return description.Trim();
        }

        return null;
    }

    /// <summary>
    ///     Infers the sub-boundary path from a member's namespace relative to the boundary namespace.
    ///     Returns null if the member is at boundary level (no sub-boundary).
    /// </summary>
    /// <param name="boundaryNamespace">The namespace where the [Boundary] class lives.</param>
    /// <param name="memberNamespace">The namespace of the action/mutation member.</param>
    /// <returns>Sub-boundary path (e.g. "Guests" or "Properties.Photos"), or null if flat.</returns>
    public static string? InferSubBoundary(string boundaryNamespace, string memberNamespace)
    {
        // Determine the effective prefix: direct or sibling (parent) match
        var prefix = ResolveEffectivePrefix(boundaryNamespace, memberNamespace);
        if (prefix is null)
            return null;

        // Extract the relative path after the prefix
        if (memberNamespace.Length <= prefix.Length)
            return null;

        // Skip the dot after prefix
        var relative = memberNamespace.Substring(prefix.Length + 1);
        var segments = relative.Split('.');

        // Collect segments until we hit an operation type segment or run out
        var subSegments = new List<string>();
        foreach (var segment in segments)
        {
            if (OperationTypeSegments.Contains(segment))
                break;
            subSegments.Add(segment);
        }

        return subSegments.Count > 0 ? string.Join(".", subSegments) : null;
    }

    private static string? ResolveEffectivePrefix(string boundaryNamespace, string memberNamespace)
    {
        // Direct match: member namespace starts with boundary namespace
        if (IsNamespacePrefixOf(boundaryNamespace, memberNamespace))
            return boundaryNamespace;

        // Sibling match, only for a boundary class filed under an operation-type folder
        // (Booking.Actions → Booking). For a boundary at Showcase.Booking the parent is Showcase, and
        // reading Showcase.Billing.Mutations against it produced the group "Billing" — an API invented
        // from a namespace that has nothing to do with the boundary. A member outside the boundary's
        // namespace has no segment between the boundary and its operation type, so it has no group.
        var lastDot = boundaryNamespace.LastIndexOf('.');
        if (lastDot <= 0 || !OperationTypeSegments.Contains(boundaryNamespace.Substring(lastDot + 1)))
            return null;

        var parentNs = boundaryNamespace.Substring(0, lastDot);
        return IsNamespacePrefixOf(parentNs, memberNamespace) ? parentNs : null;
    }

    private static bool IsNamespacePrefixOf(string prefix, string ns)
        => ns.StartsWith(prefix, StringComparison.Ordinal) &&
           (ns.Length == prefix.Length || ns[prefix.Length] == '.');
}
