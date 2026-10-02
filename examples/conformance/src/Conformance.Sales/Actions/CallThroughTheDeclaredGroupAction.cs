using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Conformance.Sales.Actions;

/// <summary>
///     Reaches an operation through the group it <b>declared</b>, not through the one the namespace
///     would have inferred.
/// </summary>
/// <remarks>
///     <para>
///         <c>RenameOrderMutation</c> sits in <c>Conformance.Sales.Mutations</c>, a flat namespace:
///         without <c>[SubBoundary(Name = "References")]</c> inference produces nothing and the operation
///         would sit on the root, that is <c>_sales.RenameOrder(…)</c>. The path written below —
///         <c>_sales.References.RenameOrder(…)</c> — exists only because someone wrote the name, and it
///         is the reason this action exists: it <b>does not compile</b> if the declared group stops
///         being generated.
///     </para>
///     <para>
///         ⚠️ Which is also how it is measured. Removing the attribute does not turn an assertion red: it
///         turns the <em>compiler</em> red, on this line. An HTTP cell next to it checks that the call
///         really arrives, because «compiles» and «works» are two things.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[Endpoint(HttpVerb.Post, "api/orders/through-the-declared-group")]
public partial class CallThroughTheDeclaredGroupAction : VoidDomainAction
{
    private ISalesInternalActions _sales = null!;

    /// <summary>The order to rename.</summary>
    public required Guid OrderId { get; init; }

    /// <summary>The new reference, which the case reads back to know the call arrived.</summary>
    public required string Reference { get; init; }

    /// <inheritdoc />
    public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)
    {
        var result = await _sales.References
            .RenameOrder(new Mutations.RenameOrderMutation { Id = OrderId, Reference = Reference }, ct)
            .ConfigureAwait(false);

        return result.IsFailure ? VoidResult<IError>.Failure(result.Error) : Success;
    }
}
