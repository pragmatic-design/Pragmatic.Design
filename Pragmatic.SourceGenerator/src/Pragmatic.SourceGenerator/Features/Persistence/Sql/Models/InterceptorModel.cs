using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     One interceptor: the <c>[InterceptsLocation]</c> attribute of its call site and an expression-bodied static method. The parameters are
///     declarations, the receiver's first for an operator (<c>this in SqlQuery&lt;T&gt; q</c>); <see cref="Body" /> is what the call becomes
///     (the slot stored, the chain retyped, the plan run). <see cref="Constraint" /> carries its leading space; <see cref="Comment" /> is the
///     call site's origin, above the attribute.
/// </summary>
internal sealed record InterceptorModel(
    string Attribute,
    string ReturnType,
    string Name,
    string TypeParameters,
    EquatableArray<string> Parameters,
    string Body,
    string Constraint = "",
    string? Comment = null,
    bool BodyOnNextLine = false);
