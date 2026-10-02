using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     A property of an entity, with its classification when it has one.
/// </summary>
/// <remarks>
///     Unclassified properties are kept rather than filtered out. PRAG2903 has to be reported
///     <em>against</em> them, and a transform that drops what it cannot model leaves the user with
///     silence instead of a reason.
/// </remarks>
internal sealed record ClassifiedPropertyModel
{
    /// <summary>
    ///     How the property is named everywhere a person reads it: the plain name on the entity's own
    ///     face, and the path below it otherwise — <c>Identity.Email</c>.
    /// </summary>
    /// <remarks>
    ///     The path and not the leaf, because the leaf answers the wrong question. "Email" in a
    ///     diagnostic, in the Article 30 register or in an access response points at a type the reader
    ///     has to go and find: there are as many <c>Email</c>s as there are owned records.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>
    ///     The chain of owning members this property sits below, or empty on the entity's own face.
    /// </summary>
    /// <remarks>
    ///     Kept apart from <see cref="Name" /> because generated code needs the hops and a message does
    ///     not: reading is null-conditional through each of them, and writing is guarded by all of them.
    /// </remarks>
    public string OwnerPath { get; init; } = string.Empty;

    /// <summary>The property's own name, without the path that leads to it.</summary>
    public string MemberName => OwnerPath.Length == 0
        ? Name
        : Name.Substring(OwnerPath.Length + 1);

    /// <summary>True when the property belongs to something the entity owns rather than to the entity.</summary>
    public bool IsNested => OwnerPath.Length > 0;

    /// <summary>
    ///     Reads the property from <paramref name="instance" />, through whatever owns it.
    /// </summary>
    /// <remarks>
    ///     Null-conditional at every hop: an owned reference can legitimately be absent — Time off's
    ///     employee has no identity record until HR opens the account — and the export of a subject
    ///     whose optional record is null is an empty value, not an exception.
    /// </remarks>
    public string ReadFrom(string instance)
        => OwnerPath.Length == 0
            ? $"{instance}.{Name}"
            : instance + "." + OwnerPath.Replace(".", "?.") + "?." + MemberName;

    /// <summary>Where a write lands, once <see cref="WriteGuard" /> has held.</summary>
    public string WriteTarget(string instance) => $"{instance}.{Name}";

    /// <summary>
    ///     The condition under which the write is reachable, or null on the entity's own face.
    /// </summary>
    /// <remarks>
    ///     Every hop, and not only the last: <c>a.B.C</c> needs both. Written as a conjunction of plain
    ///     null checks rather than a null-conditional chain because the compiler's flow analysis carries
    ///     the result into the assignment — which is what keeps the generated plan free of CS8602 under
    ///     <c>--warnaserror</c>.
    /// </remarks>
    public string? WriteGuard(string instance)
    {
        if (OwnerPath.Length == 0)
            return null;

        var hops = OwnerPath.Split('.');
        var conditions = new List<string>(hops.Length);
        var reached = instance;

        foreach (var hop in hops)
        {
            reached += "." + hop;
            conditions.Add($"{reached} is not null");
        }

        return string.Join(" && ", conditions);
    }

    /// <summary>Display string of the property type, for diagnostic messages.</summary>
    public required string TypeDisplay { get; init; }

    /// <summary>The classification, or null when the property carries none.</summary>
    public PersonalDataModel? Classification { get; init; }

    /// <summary>
    ///     True when code outside the declaring type can assign the property.
    /// </summary>
    /// <remarks>
    ///     False for the shape the framework recommends — <c>{ get; private set; }</c> — and equally for
    ///     <c>init</c>, which is public but only assignable from a constructor or an object initializer.
    ///     Anything that writes to the entity has to know this: assigning to a setter it cannot reach is
    ///     not a runtime failure, it is generated code that does not compile.
    /// </remarks>
    public bool IsPubliclySettable { get; init; }

    /// <summary>Where the property is declared, for reporting against it.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>True when the property is classified as personal data.</summary>
    public bool IsClassified => Classification is not null;

    /// <summary>
    ///     Why a person decided this property is not personal data, when they said so.
    /// </summary>
    /// <remarks>
    ///     PRAG2903 asks for a decision, and its own message offers "classify it, or mark it as
    ///     non-personal" — but until <c>[NotPersonalData]</c> existed there was no way to say no, so
    ///     the only way to satisfy the diagnostic was to classify a tenant id or a status code as
    ///     something it is not. A wrong entry in the processing register is worse than a missing one.
    /// </remarks>
    public string? NotPersonalReason { get; init; }

    /// <summary>
    ///     A property that carries free text and has not been classified either way.
    /// </summary>
    /// <remarks>
    ///     Only string-shaped properties are candidates: an <c>int Quantity</c> on an order is not
    ///     something anyone needs to rule on, and demanding a decision about it would bury the ones
    ///     that matter.
    /// </remarks>
    public bool NeedsAClassificationDecision =>
        !IsClassified && NotPersonalReason is null && TypeDisplay is "string" or "string?";

    /// <summary>
    ///     True when the declared type cannot hold null, so an erasure that assigns null to it cannot
    ///     succeed.
    /// </summary>
    /// <remarks>
    ///     Read off the C# type, which is all the generator has: the column's nullability is EF's to
    ///     decide and is not visible here. By EF's own convention a non-nullable property maps to a
    ///     NOT NULL column, so this catches the case that actually occurs; a property mapped nullable
    ///     against a non-nullable type is the rarer, deliberate exception and is not detected.
    /// </remarks>
    public bool IsNonNullable => !TypeDisplay.EndsWith("?", System.StringComparison.Ordinal);

    /// <summary>True when the declared type is a reference type, so its default is null.</summary>
    public bool IsReferenceType { get; init; }

    /// <summary>
    ///     The expression <c>Anonymize</c> writes: a value that identifies nobody and that the column
    ///     accepts — or null when the type has none.
    /// </summary>
    /// <remarks>
    ///     Null wherever the type can hold it, and a value type's default, which is a value. A
    ///     non-nullable string gets the empty string: <c>default!</c> there would write null into a
    ///     NOT NULL column, and the erasure would fail at SaveChanges. Any other
    ///     non-nullable reference type has no value that is both anonymous and generic, and PRAG2912
    ///     says so instead of inventing one.
    /// </remarks>
    public string? AnonymousValue =>
        !IsNonNullable || !IsReferenceType ? "default!"
        : TypeDisplay == "string" ? "\"\""
        : null;
}
