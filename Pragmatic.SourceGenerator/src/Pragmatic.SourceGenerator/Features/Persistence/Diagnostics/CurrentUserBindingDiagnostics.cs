using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     <c>PRAG0730-PRAG0731</c>: a property of a query, an action or a mutation bound from the caller with <c>[FromCurrentUser]</c>.
/// </summary>
/// <remarks>
///     Beside <see cref="QueryPipelineDiagnostics" /> rather than in it, like the other families of the
///     range that live where the code that emits them does: these are reported by the invoker pass, which
///     is the one that sees the <c>[PragmaticUser]</c> entity.
/// </remarks>
internal static class CurrentUserBindingDiagnostics
{
    /// <summary>
    ///     A <c>[FromCurrentUser]</c> property a caller can write. The invoker overwrites it, so a value
    ///     the caller sends is dropped — and the property still reads, in the type and in every document
    ///     generated from it, as an input.
    /// </summary>
    public static readonly DiagnosticDescriptor BoundPropertyIsSettable = DiagnosticFactory.Error(
        "PRAG0730",
        "A [FromCurrentUser] property must be set by the invoker alone",
        "'{0}.{1}' is [FromCurrentUser] and its caller can set it: declare it "
        + "'{{ get; private set; }}', so the generated invoker is the only one to write it",
        "The invoker fills the property from the caller after validation and authorization. A public, "
        + "internal or init setter lets an in-process caller write a value the invoker then overwrites, "
        + "which reads as an input and is not one. A private setter is reachable from the generated "
        + "nested invoker and from nowhere else.");

    /// <summary>
    ///     A <c>[FromCurrentUser]</c> binding the invoker cannot write: the reason is the last argument.
    /// </summary>
    public static readonly DiagnosticDescriptor BindingCannotBeGenerated = DiagnosticFactory.Error(
        "PRAG0731",
        "The [FromCurrentUser] binding cannot be generated",
        "'{0}.{1}' cannot be bound from the current user: {2}",
        "Without a member, [FromCurrentUser] binds ICurrentUser.Id to a string property. With one — "
        + "[FromCurrentUser(nameof(Employee.Id))] — it binds that member of the [PragmaticUser] entity in "
        + "this compilation, read through its generated resolver, to a property of the member's type.");
}
