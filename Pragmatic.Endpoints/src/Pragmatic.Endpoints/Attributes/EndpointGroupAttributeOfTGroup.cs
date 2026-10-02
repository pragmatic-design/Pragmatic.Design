namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Places the decorated endpoint — or nested group — inside <typeparamref name="TGroup" />.
/// </summary>
/// <remarks>
///     <para>
///         Membership is one idea, so on a type it has one spelling: there is no <c>Group</c> on
///         <c>[Endpoint]</c> and no <c>Parent</c> on the group itself. The one other place a group is named is <c>[ExposeEndpoint&lt;TAction, TGroup&gt;]</c>
///         on a module, which exposes an action that is not the module's own type to decorate — and is
///         mapped inside <typeparamref name="TGroup" /> exactly as a member endpoint is.
///     </para>
///     <para>
///         The non-generic <see cref="EndpointGroupAttribute" /> declares a group and its route prefix;
///         this one joins it. They share a name because they are the same noun in two grammatical
///         roles, and the arity tells them apart. A nested group carries both.
///     </para>
///     <example>
///         <code>
/// [EndpointGroup("/api/guests", Tag = "Guests")]
/// public sealed class GuestsGroup;
///
/// [EndpointGroup("/vip")]
/// [EndpointGroup&lt;GuestsGroup&gt;]              // nested: /api/guests/vip
/// public sealed class VipGuestsGroup;
///
/// [Endpoint(HttpVerb.Get, "/{id}")]
/// [EndpointGroup&lt;GuestsGroup&gt;]              // published at /api/guests/{id}
/// public partial class GetGuestEndpoint : DomainAction&lt;GuestId&gt; { }
/// </code>
///     </example>
/// </remarks>
/// <typeparam name="TGroup">The group type, itself decorated with <see cref="EndpointGroupAttribute" />.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EndpointGroupAttribute<TGroup> : Attribute
    where TGroup : class;
