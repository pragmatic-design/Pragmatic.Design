using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     An entity as the privacy analysis sees it: whether it is a subject, how it reaches one, and what
///     its properties are classified as.
/// </summary>
internal sealed record PrivacyEntityModel
{
    /// <summary>Fully qualified type name, without the <c>global::</c> prefix.</summary>
    public required string FullTypeName { get; init; }

    /// <summary>Simple type name, for messages and generated member names.</summary>
    public required string TypeName { get; init; }

    /// <summary>Containing namespace; empty for the global namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The identifier property when this entity is a <c>[DataSubject]</c>; null otherwise.
    /// </summary>
    public string? SubjectIdentifier { get; init; }

    /// <summary>
    ///     The property named by <c>[LinksToSubject]</c>; null when the entity declares no path.
    /// </summary>
    public string? SubjectPath { get; init; }

    /// <summary>
    ///     The type the path leads to, resolved from the named property. Null when the path could not be
    ///     followed — which is PRAG2906, not a reason to drop the entity.
    /// </summary>
    public string? SubjectPathTargetFullTypeName { get; init; }

    /// <summary>Every property, classified or not.</summary>
    public EquatableArray<ClassifiedPropertyModel> Properties { get; init; }
        = EquatableArray<ClassifiedPropertyModel>.Empty;

    /// <summary>
    ///     The types whose members are folded into <see cref="Properties" />: what this entity owns, and
    ///     what it inherits.
    /// </summary>
    /// <remarks>
    ///     What it is for: a type reached only as an owned member or as a base is analysed
    ///     <em>through the entity that holds it</em>, where the path says which record a column belongs
    ///     to. Left as an entity of its own it would be reported twice on the same line, and — since it
    ///     declares no path to a subject — PRAG2900 would say its classified data can never be erased,
    ///     which is the opposite of what owning or inheriting it means.
    /// </remarks>
    public EquatableArray<string> FoldedTypeFullNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Where the type is declared, for reporting against it.</summary>
    public LocationInfo? Location { get; init; }

    /// <summary>
    ///     True when the type carries <c>[Entity]</c>, so Pragmatic.Persistence emits an
    ///     internal <c>Set{Property}</c> for each of its private-setter properties.
    /// </summary>
    /// <remarks>
    ///     Read from the attribute rather than from the members: in the compilation that declares the
    ///     entity those setters do not exist yet — the generator that creates them runs alongside this
    ///     one — so the only thing that can be observed is the marker that will cause them to exist.
    /// </remarks>
    public bool IsPersistenceEntity { get; init; }

    /// <summary>
    ///     The boundary type named by <c>[BelongsTo&lt;TBoundary&gt;]</c>; null when the entity declares
    ///     none.
    /// </summary>
    /// <remarks>
    ///     The key the boundary's <c>DbContext</c> is registered under, so a generated adapter asks for
    ///     the same context the entity's repository does. Without it the adapter falls back to the
    ///     unkeyed <c>DbContext</c> — the only thing a single-context application registers.
    /// </remarks>
    public string? BoundaryTypeFullName { get; init; }

    /// <summary>True when this entity is itself a data subject.</summary>
    public bool IsSubject => SubjectIdentifier is not null;

    /// <summary>True when any property is classified as personal data.</summary>
    public bool HasPersonalData => Properties.Any(p => p.IsClassified);

    /// <summary>
    ///     A declared path that could not be resolved to a type — PRAG2906. Distinguished from having no
    ///     path at all, because the two have different causes and different fixes.
    /// </summary>
    public bool HasUnresolvablePath => SubjectPath is not null && SubjectPathTargetFullTypeName is null;
}
