namespace Pragmatic.Privacy;

/// <summary>
///     Records that a property on a subject-reachable entity is deliberately <b>not</b> personal data.
/// </summary>
/// <remarks>
///     <para>
///         <c>PRAG2903</c> refuses an unclassified string on an entity reachable from a
///         <c>[DataSubject]</c>, because an unclassified field is indistinguishable from one nobody
///         thought about. Some of those fields genuinely are not about a person — a tenant id, a
///         status code, an external reference — and without this attribute the only way to satisfy
///         the diagnostic would be to classify them as something they are not, which is worse than
///         leaving them blank: it puts a wrong entry in the processing register.
///     </para>
///     <para>
///         So the point is the same as the diagnostic's: <b>the decision is recorded</b>. This says a
///         person looked and concluded no; <c>[PersonalData]</c> says a person looked and concluded
///         yes. Neither is silence.
///     </para>
///     <para>
///         ⚠️ It classifies, it does not exempt. A field that <em>can</em> hold personal data — free
///         text a user types, a note, a description — is not made safe by declaring it isn't. Use
///         <c>[NotLogged]</c> for those: the risk there is leaking into logs, not an erasure plan that
///         cannot act on prose.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [NotPersonalData("Tenant isolation key, assigned by the platform.")]
/// public string TenantId { get; set; } = "";
///     </code>
/// </example>
/// <param name="reason">
///     Why it is not personal data. Required, and it appears in the processing register beside the
///     classified fields: "someone decided" is only useful with what they decided and why.
/// </param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NotPersonalDataAttribute(string reason) : Attribute
{
    /// <summary>Why this property is not personal data.</summary>
    public string Reason { get; } = reason;
}
