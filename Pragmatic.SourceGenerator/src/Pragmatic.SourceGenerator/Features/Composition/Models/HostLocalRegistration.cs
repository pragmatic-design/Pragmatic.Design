// Pragmatic.SourceGenerator - Composition - Registrations contributed by the compilation itself

using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Builds the <see cref="MetadataEntry" /> a feature hands to the host for a registration it
///     generates into <i>this</i> compilation.
/// </summary>
/// <remarks>
///     <para>
///         For a referenced assembly the host learns which <c>Add*</c> to call by reading that
///         assembly's <c>[assembly: PragmaticMetadata]</c>. For a framework type declared in the host
///         project that channel does not exist: the attribute is emitted by this same generator run, so
///         it is not on the symbol yet and <c>MetadataReader.ReadFromReferences</c> — which only walks
///         <c>compilation.References</c> — could not see it even if it were.
///     </para>
///     <para>
///         So the feature that renders the registration states its entry point directly, exactly as
///         <c>CachingFeature</c> hands Composition the event handlers it generates. The name comes from
///         <see cref="GeneratedRegistrationNames" />, the same place the rendering template reads it.
///     </para>
/// </remarks>
internal static class HostLocalRegistration
{
    /// <summary>
    ///     A local registration entry. <paramref name="category" /> is a
    ///     <see cref="MetadataCategoryIds" /> value and decides which host call site picks it up;
    ///     <paramref name="registrationMethodFqn" /> is the <c>Namespace.Class.Method</c> to call.
    /// </summary>
    public static MetadataEntry Create(string category, string schemaVersion, string registrationMethodFqn)
        => new()
        {
            Category = category,
            SchemaVersion = schemaVersion,
            RegistrationMethod = registrationMethodFqn,
            // The host reads only Category and RegistrationMethod off these entries; the payload a
            // referenced assembly carries (handler lists, job counts, …) describes types the host has
            // to name, and it names none of these — it calls one generated method per entry.
            JsonData = "{}"
        };

    /// <summary>
    ///     A local entry that carries a payload instead of an entry point, for the features whose host
    ///     wiring is rendered <i>inline</i> from the models — Actions, Endpoints, Persistence — rather
    ///     than as a call to a generated <c>Add*</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="jsonData" /> must be the same document the feature's metadata template
    ///         writes into <c>[assembly: PragmaticMetadata]</c>, because the host parses it with the same
    ///         <c>MetadataReader.Extract*</c> the referenced-assembly path uses. Building it twice from
    ///         two places is how the two shapes drift apart, so callers render it from the template.
    ///     </para>
    ///     <para>
    ///         <c>RegistrationMethod</c> is empty by default, because these documents declare
    ///         <c>"registrationMethod": null</c> and every host call site that dispatches on one skips
    ///         an empty one. The rule behind it is not "payload means no method": it is that a
    ///         host-declared type must get <b>exactly</b> the wiring the control group gets. So a
    ///         feature whose referenced-assembly path is a call passes its methods here too, as Actions
    ///         does.
    ///     </para>
    /// </remarks>
    public static MetadataEntry CreatePayload(
        string category,
        string schemaVersion,
        string jsonData,
        string registrationMethod = "",
        string secondaryRegistrationMethod = "")
        => new()
        {
            Category = category,
            SchemaVersion = schemaVersion,
            RegistrationMethod = registrationMethod,
            SecondaryRegistrationMethod = secondaryRegistrationMethod,
            JsonData = jsonData
        };
}
