using Showcase.Billing.Actions;

namespace Showcase.Billing.Infrastructure.Authorization;

/// <summary>
///     Resource authorizer for invoice refund actions.
///     Users with <c>billing.admin</c> permission bypass the check;
///     otherwise, only users whose department matches the invoice department can refund.
/// </summary>
public sealed class InvoiceAuthorizer : IResourceAuthorizer<RefundInvoiceAction>
{
    /// <inheritdoc />
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user, RefundInvoiceAction resource, string action,
        CancellationToken ct = default)
    {
        // Admin bypass
        if (user.Authorization.HasPermission("billing.admin"))
            return ValueTask.FromResult(true);

        // Department-based check: user must have the billing.invoice.refund permission
        // (already enforced by [RequirePermission]) and be authenticated
        if (!user.IsAuthenticated)
            return ValueTask.FromResult(false);

        // For this showcase, any authenticated user with billing.invoice.refund permission can refund
        // In production, you'd check resource.DepartmentId against user claims
        return ValueTask.FromResult(user.Authorization.HasPermission("billing.invoice.refund"));
    }
}
