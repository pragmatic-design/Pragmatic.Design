namespace Pragmatic.Events;

/// <summary>
///     Marks a <b>domain event</b> as a public integration event — part of the cross-boundary contract,
///     surfaced in the generated AsyncAPI.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>This marks a domain event; it does not make one.</b> An integration event <em>is</em> a
///         domain event that is also published — <see cref="IIntegrationEvent"/> is declared as
///         <c>IIntegrationEvent : IDomainEvent</c> — and every consumer of this marker starts from
///         <see cref="IDomainEvent"/>. So the two are <b>not</b> alternatives: implementing
///         <see cref="IIntegrationEvent"/> says both things at once, while this attribute says only the
///         second and needs the first to be true already.
///     </para>
///     <para>
///         ⚠️ On a type that does not implement <see cref="IDomainEvent"/> the marker is read by
///         nobody: the type stays out of the generated AsyncAPI document — <c>AsyncApiFeature</c>
///         catalogues types implementing <see cref="IDomainEvent"/> — and out of the transactional
///         outbox, which captures events raised by tracked <c>IHasDomainEvents</c> entities. The generator
///         reports it as <b>PRAG0836</b>. The attribute is not an alternative to implementing
///         <c>IIntegrationEvent</c>: a record marked only <c>[PublicEvent]</c> and published to a broker
///         has no published contract at all.
///     </para>
/// </remarks>
/// <remarks>
///     The contract is owned by the publishing boundary. Treat a public event as a stable API: prefer
///     additive evolution and mark removals with <c>[ObsoleteEvent]</c>.
/// </remarks>
/// <remarks>
///     <b>Where this is consumed.</b> <c>AsyncApiFeature</c> alone. It is one of the markers that put
///     an event into the generated AsyncAPI document; nothing reads it at run time, and marking an
///     event public changes no behaviour — only what the published contract says.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PublicEventAttribute : Attribute;
