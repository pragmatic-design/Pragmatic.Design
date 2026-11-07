using System.Collections.Generic;
using System.Linq;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The parameter lists of the <c>Create</c> overloads <c>EntityCreateTemplate</c> writes for an entity, as text: one place, read by the
///     template for the entity it writes and by the transform for that entity's base entities (#540), so the two cannot disagree on what hides
///     what.
/// </summary>
internal static class CreateSignature
{
    private const string TimeProvider = "global::System.TimeProvider?";

    /// <summary>
    ///     <c>Create(required…, [TimeProvider?])</c> always, and <c>Create([TimeProvider?])</c> as well when there are required properties.
    /// </summary>
    public static IEnumerable<string> Of(IReadOnlyList<string> requiredTypes, bool auditable)
    {
        string[] tail = auditable ? [TimeProvider] : [];
        yield return Join(requiredTypes.Concat(tail));
        if (requiredTypes.Count > 0)
            yield return Join(tail);
    }

    /// <summary>The overload with the required properties as parameters.</summary>
    public static string Full(IReadOnlyList<string> requiredTypes, bool auditable)
        => Join(auditable ? requiredTypes.Concat([TimeProvider]) : requiredTypes);

    /// <summary>The overload with no required property as a parameter.</summary>
    public static string Parameterless(bool auditable) => auditable ? TimeProvider : "";

    private static string Join(IEnumerable<string> types) => string.Join(",", types);
}
