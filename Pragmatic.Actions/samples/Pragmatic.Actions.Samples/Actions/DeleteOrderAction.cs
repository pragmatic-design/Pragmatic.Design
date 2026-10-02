using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Actions.Samples.Actions;

/// <summary>
///     Deletes an order. Authorization for this action is demonstrated declaratively via the
///     <c>[RequirePolicy&lt;CanManageOrdersPolicy&gt;]</c> pattern shown in <c>AuthorizationSample</c>.
/// </summary>
/// <remarks>
///     The <c>[RequirePolicy&lt;T&gt;]</c> attribute is NOT applied here: in a single assembly that already
///     has <c>[RequirePermission]</c> actions, combining it with a <c>[RequirePolicy]</c> action triggers
///     a source-generator hint-name collision (see "BUGS FOUND" in the sample report). The attribute and
///     its <c>ResourcePolicy</c> are still exercised by type inspection in the sample.
/// </remarks>
[DomainAction]
public partial class DeleteOrderAction : VoidDomainAction
{
    /// <summary>The ID of the order to delete.</summary>
    public required Guid OrderId { get; init; }

    public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(VoidResult<IError>.Success());
}
