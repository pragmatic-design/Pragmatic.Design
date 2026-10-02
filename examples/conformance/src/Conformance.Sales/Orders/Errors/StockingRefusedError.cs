using Pragmatic.Result.Http;

namespace Conformance.Sales.Errors;

/// <summary>
///     The refusal <see cref="Mutations.StockOrderThroughMutation"/> produces <b>after</b> the catalog
///     committed.
/// </summary>
/// <remarks>
///     A type of its own and not a <c>BusinessRuleError.Create(…)</c>: it is what the case looks for in
///     the response to know that the outer step failed, and not the catalog — the difference between an
///     undo that ran and an undo never registered.
/// </remarks>
public sealed record StockingRefusedError() : BusinessRuleError("stocking-refused");
