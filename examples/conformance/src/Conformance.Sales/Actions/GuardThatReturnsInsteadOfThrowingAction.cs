using Conformance.Sales.Errors;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;
using Check = Pragmatic.Ensure.Result.Check;

namespace Conformance.Sales.Actions;

/// <summary>
///     A guard that <b>returns</b> instead of throwing: the seam between «Result over exceptions» and
///     «Ensure for guards».
/// </summary>
/// <remarks>
///     <para>
///         The framework's two rules meet here. <c>Ensure.ThrowIfNullOrWhiteSpace</c> would throw, and an
///         exception from an action body is a 500: right for a programming invariant — a null
///         repository, a bug — and wrong for data coming from the caller. <c>Check</c> performs the same
///         check and returns the error it is given, which here is a 422 with a domain rule.
///     </para>
///     <para>
///         ⚠️ <b>No <c>[Required]</c> on <c>Label</c>, on purpose.</b> With the declarative rule the
///         pipeline would refuse first, the body would not start, and the case would measure validation
///         instead of the guard — which is another cell, already covered by <c>CheckEveryRuleAction</c>.
///         Here the subject is what a guard does inside the body.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[NoValidation]
[Endpoint(HttpVerb.Post, "api/labels/guarded")]
public partial class GuardThatReturnsInsteadOfThrowingAction : DomainAction<string>
{
    public string Label { get; init; } = "";

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
    {
        var named = Check.NotNullOrWhiteSpace(Label, new LabelRequiredError());

        return Task.FromResult(named.IsFailure
            ? Result<string, IError>.Failure(named.Error)
            : Result<string, IError>.Success(Label));
    }
}
