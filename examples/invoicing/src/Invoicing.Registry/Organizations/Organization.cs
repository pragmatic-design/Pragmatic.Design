using Invoicing.Registry.Events;
using Pragmatic.Authoring;
using Pragmatic.MultiTenancy;

namespace Invoicing.Registry.Entities;

/// <summary>
///     A company that bills with this service, and the tenant every other row belongs to.
/// </summary>
/// <remarks>
///     <para>
///         The one entity in the application that is <b>not</b> <c>ITenantEntity</c>: it is what a tenant
///         id names, so belonging to a tenant would be circular. For the same reason its <c>Slug</c> is
///         unique <b>globally</b> and not per tenant — the default scope would ask "unique within which
///         tenant?" about the value that answers that question.
///     </para>
///     <para>
///         <c>State</c> is read on every request by the tenant middleware, through
///         <c>OrganizationTenantStore</c>: a suspended company is not served, and that is the whole
///         mechanism — no flag anybody has to remember to check.
///     </para>
/// </remarks>
[Entity]
[Auditable]
[Audited]
[ConcurrencyAware]
// Onboarding is exactly the creation of this row — the company exists when its row does, and there is
// no earlier moment to announce, so the lifecycle transition is where the event belongs.
[Raises<OrganizationOnboarded>]
public partial class Organization : DomainEventSource, IEntity
{
    /// <summary>The tenant id: lowercase, short, and what the token carries in its <c>tenant_id</c> claim.</summary>
    [LogicKey(Scope = UniquenessScope.Global)]
    [Required]
    [MaxLength(40)]
    public string Slug { get; private set; } = "";

    [Required]
    [MaxLength(200)]
    public string LegalName { get; private set; } = "";

    [MaxLength(20)]
    public string VatNumber { get; private set; } = "";

    /// <summary>The address the reminders are sent from.</summary>
    [Email]
    [MaxLength(320)]
    public string SenderEmail { get; private set; } = "";

    /// <summary>What every invoice number of this company starts with — <c>ACME/2026/0001</c>.</summary>
    [Required]
    [MaxLength(10)]
    public string InvoiceNumberPrefix { get; private set; } = "";

    public PostalAddress Address { get; private set; } = new("", "", "", "");

    /// <summary>Whether this company is served. Anything but <see cref="TenantState.Active" /> is refused.</summary>
    public TenantState State { get; private set; } = TenantState.Active;

    /// <summary>Stops serving this company's people, without touching a row of its data.</summary>
    /// <remarks>
    ///     <para>
    ///         Raised here, by hand, and that is the only shape that works for a move like this one.
    ///         <c>TenantState</c> belongs to <c>Pragmatic.MultiTenancy</c>, so this module cannot put
    ///         <c>[RaisesEvent]</c> on its members the way <c>InvoiceStatus</c> does; and the entity's
    ///         <c>[Raises&lt;T&gt;]</c> covers a <b>lifecycle</b> transition — created, updated,
    ///         deleted — which a suspension is not: it is one update among others.
    ///     </para>
    ///     <para>
    ///         ⚠️ Not <c>[Raises&lt;OrganizationSuspended&gt;]</c> <b>on this method</b>, although the
    ///         attribute accepts <c>AttributeTargets.Method</c>: the generator does not wire it there —
    ///         the generated <c>Organization.LifecycleEvents.g.cs</c> carries only the lifecycle
    ///         branches — and the build refuses it with <b>PRAG2753</b>, which names this body and the
    ///         two shapes that work.
    ///     </para>
    ///     <para>
    ///         Reactivation raises nothing: the manifest declares two events for a company, and a
    ///         company coming back is the absence of the suspension rather than a third fact.
    ///     </para>
    /// </remarks>
    internal void Suspend()
    {
        SetState(TenantState.Suspended);
        RaiseEvent(new OrganizationSuspended(Id, Slug, DateTimeOffset.UtcNow));
    }

    /// <summary>Serves it again.</summary>
    internal void Reactivate() => SetState(TenantState.Active);
}
