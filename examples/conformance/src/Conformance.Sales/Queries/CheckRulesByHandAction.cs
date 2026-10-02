using Conformance.Sales.Errors;
using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;
using Pragmatic.Validation.Attributes;

namespace Conformance.Sales.Queries;

/// <summary>
///     The same declared rule as <see cref="CheckEveryRuleAction"/>, with validation taken over by the
///     body.
/// </summary>
/// <remarks>
///     <para>
///         <c>[NoValidation]</c> does not remove the rule: the generator still renders
///         <c>[MinLength(3)]</c>, and it is visible in the published contract. It removes the
///         <b>automatic execution</b> — the invoker's validation step exits early, and <c>Execute</c>
///         answers.
///     </para>
///     <para>
///         ⚠️ Why someone would do it: a rule that depends on several fields together, or that must
///         refuse with a domain error instead of the rule's key. Here it is the second: the same value
///         that over there produces <c>validation.minlength</c> under the property's name, here produces
///         <c>checked-by-hand</c> in <c>rule</c>. The two 422s can be told apart only this way, and it
///         is the distinction that says <em>who</em> refused.
///     </para>
///     <para>
///         The control is <c>CheckEveryRuleAction</c>, which carries the same <c>[MinLength(3)]</c>
///         without this attribute: <c>EveryRuleIsExecuted</c> sends the same <c>"ab"</c> and receives the
///         pipeline's key. Without that half, «refused» would also be satisfied by an attribute that does
///         nothing.
///     </para>
/// </remarks>
[DomainAction]
[AllowAnonymous]
[NoValidation]
[Endpoint(HttpVerb.Post, "api/rules/by-hand")]
public partial class CheckRulesByHandAction : DomainAction<string>
{
    [MinLength(3)]
    public required string AtLeastThree { get; init; }

    public override Task<Result<string, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(AtLeastThree.Length < 3
            ? Result<string, IError>.Failure(new HandCheckedRefusalError())
            : Result<string, IError>.Success("checked by hand"));
}
