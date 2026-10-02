namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Declares that this route belongs to no tenant, so tenant resolution does not require one.
/// </summary>
/// <remarks>
///     <para>
///         In a multi-tenant application the tenant middleware refuses a request that resolves no
///         tenant — that is the point of <c>RequireTenant</c>. A liveness probe, a version endpoint,
///         a public status page belongs to no tenant and has nothing to send.
///     </para>
///     <para>
///         ⚠️ <c>[AllowAnonymous]</c> is not enough and never was. It lifts authentication; the
///         tenant refusal happens at order 92, before the route runs and independently of who is
///         asking. So an anonymous probe was answered <b>400</b> in a multi-tenant host, and the only
///         way out was metadata a module could not declare: <c>TenantAgnosticEndpoint</c> existed and
///         was attached from exactly one place inside the framework, which is why the aggregated
///         <c>/health</c> worked and the mechanism looked reachable.
///     </para>
///     <para>
///         It relaxes the <em>requirement</em>, not the resolution: a tenant supplied on one of these
///         routes is still resolved and still published downstream, because an endpoint that does not
///         need a tenant may still be glad of one — a health endpoint that reports per tenant, a
///         document that varies by it.
///     </para>
///     <example>
///         <code>
/// [Endpoint(HttpVerb.Get, "api/status")]
/// [AllowAnonymous]
/// [TenantAgnostic]
/// public partial class StatusEndpoint { … }
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TenantAgnosticAttribute : Attribute;
