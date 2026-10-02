using Pragmatic.Result.Http;

namespace Conformance.Sales.Errors;

/// <summary>
///     The refusal a <c>Check</c> returns instead of throwing it.
/// </summary>
/// <remarks>
///     A type of its own because the case must be able to tell <b>who</b> refused: a guard that returns
///     carries this rule, a declarative rule would carry <c>validation.notempty</c>, and an <c>Ensure</c>
///     that throws would carry nothing — it would be a 500.
/// </remarks>
public sealed record LabelRequiredError() : BusinessRuleError("label-required");
