using Microsoft.CodeAnalysis;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Diagnostics;

/// <summary>
///     Diagnostics for <c>[GenerateMock&lt;T&gt;]</c> (PRAG2350-2359, inside the Testing range
///     PRAG2350-2399).
/// </summary>
/// <remarks>
///     These report what the generator will NOT do, and they exist because the alternative is worse:
///     emitting a member the compiler then rejects, leaving the author to work backwards from a
///     CS error in generated code to the declaration that caused it.
/// </remarks>
internal static class MockDiagnostics
{
    private const string Category = "Pragmatic.Testing";

    /// <summary>PRAG2350 — the mocked type must be an interface or a class that can be derived from.</summary>
    internal static readonly DiagnosticDescriptor NotAnInterface = new(
        id: "PRAG2350",
        title: "[GenerateMock<T>] requires an interface or a derivable class",
        messageFormat: "'{0}' is neither an interface nor a class that can be derived from, so no mock "
                       + "was generated. A sealed or static class cannot be subclassed; declare an "
                       + "interface, or hand-write a fake for this type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // PRAG2351 is retired and not reused. Overloaded members are not skipped: each overload gets its
    // own member, named with its parameter count.

    /// <summary>PRAG2352 — generic methods need a closed type to be configured.</summary>
    internal static readonly DiagnosticDescriptor GenericMember = new(
        id: "PRAG2352",
        title: "Generic method not configurable in generated mock",
        messageFormat: "'{0}.{1}' is generic, so it cannot be configured in a typed way: the mock "
                       + "implements it and returns default. Only ICacheStack and "
                       + "IDomainEventDispatcher are affected across this repository.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>PRAG2353 — the same type asked for twice.</summary>
    internal static readonly DiagnosticDescriptor DuplicateDeclaration = new(
        id: "PRAG2353",
        title: "Duplicate [GenerateMock<T>] declaration",
        messageFormat: "A mock for '{0}' is declared more than once in this assembly. "
                       + "The extra declarations are ignored.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
