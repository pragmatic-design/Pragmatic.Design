namespace Pragmatic.FeatureFlags;

/// <summary>
///     Refuses a query, an action or a mutation with a 404 when the named feature flag is off for the
///     caller: the generated invoker evaluates it through <see cref="IFeatureFlags" />, after validation and
///     authorization and before the read or the body.
/// </summary>
/// <remarks>
///     <para>
///         <b>404, not 403.</b> A tenant that has not bought a module is not refused a permission it lacks;
///         for it the operation does not exist, and saying so is what a route that is not there says.
///     </para>
///     <para>
///         The flag is evaluated against the ambient context — <c>IFeatureFlagContextProvider</c>, which
///         <c>AddPragmaticFeatureFlags</c> fills from the request's tenant unless the application registers
///         its own — so a tenant targeting rule (<c>FeatureFlagRule.Tenant(…)</c>) turns the operation on for
///         those tenants only. Several attributes all have to be on.
///     </para>
///     <para>
///         ⚠️ Fail closed: with no <see cref="IFeatureFlags" /> registered the operation is refused, because
///         a gate nobody can open is a gate that is shut. <c>PRAG1616</c> says so at build time, in a host
///         that does not reference <c>Pragmatic.FeatureFlags</c>.
///     </para>
///     <para>
///         <see cref="RequiresFeatureAttribute{TFlag}" /> names the flag by its <see cref="IFeatureFlag" /> type.
///     </para>
/// </remarks>
/// <param name="flagName">The flag's name in the store.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresFeatureAttribute(string flagName) : Attribute
{
    /// <summary>The flag's name in the store.</summary>
    public string FlagName { get; } = flagName;
}
