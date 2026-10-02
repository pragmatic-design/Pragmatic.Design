using Pragmatic.Actions.Samples.Actions;
using Pragmatic.Authorization;
using Pragmatic.Identity;

namespace Pragmatic.Actions.Samples.Authorization;

/// <summary>
///     Resource-level authorizer for <see cref="ApproveOrderAction" />. Demonstrates instance-level
///     authorization beyond a simple permission check: "can THIS user approve THIS specific order?".
/// </summary>
/// <remarks>
///     Register with <c>services.AddResourceAuthorizer&lt;ApproveOrderAuthorizer, ApproveOrderAction&gt;()</c>
///     (or via <c>AuthorizationBuilder.AddResourceAuthorizer</c>). The <c>ResourceAuthorizationFilter</c>
///     (Order 250) resolves it after the permission check and before the transaction.
/// </remarks>
public sealed class ApproveOrderAuthorizer : IResourceAuthorizer<ApproveOrderAction>
{
    /// <summary>
    ///     Example rule: anonymous users can never approve; authenticated users can.
    ///     A real implementation would inspect ownership, tenant, or order state.
    /// </summary>
    public ValueTask<bool> CanAccessAsync(
        ICurrentUser user,
        ApproveOrderAction resource,
        string operation,
        CancellationToken ct = default)
    {
        var allowed = user.IsAuthenticated && resource.OrderId != Guid.Empty;
        return ValueTask.FromResult(allowed);
    }
}
