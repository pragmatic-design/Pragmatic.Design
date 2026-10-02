namespace Pragmatic.Patch.Attributes;

/// <summary>
///     Keeps a property out of a generated patch, when the domain says it cannot be corrected.
/// </summary>
/// <remarks>
///     <para>
///         The shape of a patch is derived from the entity, so every settable property is patchable.
///         Infrastructure columns are already excluded — id, audit, soft-delete, and tenant, owner and
///         scope keys, which a patch must never touch or it becomes a mass-assignment hole — but a
///         <b>domain</b> invariant had nowhere to be declared.
///     </para>
///     <para>
///         The case that produced this: a glossary term whose word every past sighting was resolved
///         against. Changing it in place re-points those sightings at a word that was never in those
///         texts, so the endpoint refused it with 422 — while the published contract went on offering
///         the field, and a generated client went on presenting it to the caller. The refusal and the
///         contract disagreed, and only one of them was checked by anything.
///     </para>
///     <para>
///         Names are given with <c>nameof</c>, so a rename that forgets this line does not compile.
///     </para>
/// </remarks>
/// <example>
///     <code>
///     [GeneratePatch&lt;KnowledgeItem&gt;]
///     [PatchIgnore(nameof(KnowledgeItem.Term))]
///     public partial record CorrectTermPatch;
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class PatchIgnoreAttribute : Attribute
{
    /// <summary>Creates the attribute for one or more property names.</summary>
    /// <param name="propertyNames">
    ///     The properties to leave out. Pass them with <c>nameof</c>; a name that matches nothing on
    ///     the entity is reported as <c>PRAG2206</c> rather than ignored, because a typo here silently
    ///     restores the very property the author meant to protect.
    /// </param>
    public PatchIgnoreAttribute(params string[] propertyNames)
    {
        PropertyNames = propertyNames;
    }

    /// <summary>The properties to leave out of the generated patch.</summary>
    public string[] PropertyNames { get; }
}
