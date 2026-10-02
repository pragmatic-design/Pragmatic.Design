namespace Invoicing.Billing.Errors;

/// <summary>
///     The sweep was asked to run with no company in scope.
/// </summary>
/// <remarks>
///     ⚠️ This is the error that exists because of how a multi-tenant read fails: outside a request nothing
///     resolves a tenant, and the generated filter is fail-closed — the query returns <b>zero rows</b>, not
///     an error. A sweep that trusted the count would report success over an empty result and tell every
///     customer their queue is empty. Declining beats counting to zero.
/// </remarks>
public sealed partial record NoTenantResolvedError : Error
{
    public override string Code => "NO_TENANT_RESOLVED";

    /// <summary>500: nobody asked for this over HTTP — it is a wiring mistake, not a bad request.</summary>
    public override int StatusCode => 500;
}
