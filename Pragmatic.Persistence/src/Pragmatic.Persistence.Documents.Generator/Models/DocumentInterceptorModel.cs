namespace Pragmatic.Persistence.Documents.Generator.Models;

/// <summary>
///     One intercepted call of a document chain: an operator stores its argument in the chain's slot, a terminal runs its plan. The method's
///     parts and the plan's initializer are the translation's, as text.
/// </summary>
/// <param name="Attribute">The <c>[InterceptsLocation]</c> attribute of the call site.</param>
/// <param name="Returns">The method's return type.</param>
/// <param name="Name">The intercepted method's name, the method's prefix.</param>
/// <param name="Index">The interceptor's number in the file, the method's and its plan's suffix.</param>
/// <param name="TypeParameters">The method's type parameter list, with its brackets; empty without one.</param>
/// <param name="Parameters">The method's parameters, the chain first.</param>
/// <param name="Body">The method's expression body.</param>
/// <param name="PlanEntity">A terminal's document type, the plan's type argument; null for an operator.</param>
/// <param name="PlanInitializer">A terminal's plan; null for an operator.</param>
/// <param name="JoinInner">A join's inner document type; null without a join.</param>
/// <param name="JoinInitializer">A join's description; null without a join.</param>
internal sealed record DocumentInterceptorModel(
    string Attribute,
    string Returns,
    string Name,
    int Index,
    string TypeParameters,
    string Parameters,
    string Body,
    string? PlanEntity = null,
    string? PlanInitializer = null,
    string? JoinInner = null,
    string? JoinInitializer = null);
