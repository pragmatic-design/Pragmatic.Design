using Pragmatic.Result.Http;

namespace Conformance.Sales.Errors;

/// <summary>
///     The refusal <see cref="Queries.CheckRulesByHandAction"/> produces <b>from the body</b>, instead of
///     leaving it to the pipeline.
/// </summary>
/// <remarks>
///     It tells apart two 422s that would otherwise look alike: the declared rule's carries
///     <c>validation.minlength</c> under the property's name, this one carries its rule in <c>rule</c>.
///     Without a type of its own, «refused» would not say <em>who</em> refused, which is the whole
///     difference <c>[NoValidation]</c> makes.
/// </remarks>
public sealed record HandCheckedRefusalError() : BusinessRuleError("checked-by-hand");
