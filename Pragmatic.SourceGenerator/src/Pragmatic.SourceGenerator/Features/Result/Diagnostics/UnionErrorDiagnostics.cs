using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Result.Diagnostics;

/// <summary>
///     Diagnostic descriptors for a C# 15 union declared as an error set: a union that implements <c>IError</c>, whose members the generator
///     writes by delegating to the active case (#865). Range: PRAG0003-PRAG0004.
/// </summary>
internal static class UnionErrorDiagnostics
{
    /// <summary>
    ///     A union implements <c>IError</c> and leaves its members unwritten, but is not <c>partial</c>: the generator cannot add them. Without
    ///     it the compiler reports only the missing members, and nothing says the union can have them written.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionErrorNotPartial = DiagnosticFactory.Warning(
        "PRAG0003",
        "Union error set is not partial",
        "Union '{0}' implements IError but is not partial, so its IError members cannot be generated. Declare it 'partial union {0}' and " +
        "the generator writes them, each delegated to the active case.",
        "A union used as an error type implements IError by delegating every member to the case it holds. The generator writes that " +
        "delegation into a further part of the union, which needs the union to be declared partial.");

    /// <summary>
    ///     A partial union implements <c>IError</c>, and one of its cases does not: the active case is what every member delegates to, so that
    ///     case would have no code, status or title to give. Nothing is generated, and this says why.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionErrorCaseNotAnError = DiagnosticFactory.Error(
        "PRAG0004",
        "Union error case is not an error",
        "Union '{0}' implements IError, but its case '{1}' does not implement Pragmatic.Result.IError. Every case of an error set must be an " +
        "error: implement IError on '{1}', or remove it from the union.",
        "The IError members of a union are delegated to its active case, so each case must be an IError itself. The generator writes no " +
        "members while a case is not one.");
}
