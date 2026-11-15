namespace Pragmatic.MultiTenancy;

/// <summary>
///     Fills a property of a declared query, an action or a mutation with the request's tenant: the
///     generated invoker writes <see cref="ITenantContext.TenantId" /> into it after validation and
///     authorization, before the read or the body.
/// </summary>
/// <remarks>
///     <para>
///         The property is a <see cref="string" />, written <c>{ get; private set; }</c>. It is not a
///         parameter anywhere — not in the query string, the route or the body, not in the OpenAPI
///         document — so a tenant the caller sends is never the one the operation reads. On a query it is
///         not a filter either: the tenant's rows are already filtered by the tenant filter, and a
///         specification property reads this value when the query needs the id itself.
///     </para>
///     <para>
///         With no tenant resolved, a <c>string?</c> property is left <see langword="null" /> and a
///         <c>string</c> one refuses the operation with a 400: the operation said it needs a tenant, and an
///         empty string would read as one.
///     </para>
///     <para>
///         <c>PRAG0747</c> reports a binding the invoker cannot write, and <c>PRAG1615</c> a host that
///         cannot resolve a tenant at all.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [FromTenant]
/// public string Tenant { get; private set; } = "";
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FromTenantAttribute : Attribute;
