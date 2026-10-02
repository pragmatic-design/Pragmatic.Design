using Pragmatic.SourceGen;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Models;

/// <summary>A method to implement on the generated mock.</summary>
internal sealed record MockMethodModel
{
    /// <summary>The method name, as declared on the interface.</summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The name of the public member exposing it. Same as <see cref="Name"/> unless the method is
    ///     overloaded, in which case the parameter count is appended — <c>ScriptEvaluateAsync4</c> —
    ///     because two members cannot share a name and the alternative was leaving every overload
    ///     unconfigurable. <c>IDatabase.ScriptEvaluateAsync</c> is nothing but overloads.
    /// </summary>
    public required string MemberName { get; init; }

    /// <summary>The fully-qualified return type, or <c>void</c>.</summary>
    public required string ReturnType { get; init; }

    /// <summary>Whether the method returns <see langword="void"/>.</summary>
    public required bool IsVoid { get; init; }

    /// <summary>The parameters, in declaration order.</summary>
    public required EquatableArray<MockParameterModel> Parameters { get; init; }

    /// <summary>
    ///     Whether the mock exposes a configurable member for this method. False for overloads and
    ///     generic methods, which are still implemented — returning default — so the type compiles,
    ///     but cannot be configured or asserted on. The generator reports PRAG2351/PRAG2352 for each.
    /// </summary>
    public required bool Configurable { get; init; }

    /// <summary>
    ///     Whether this method has more parameters than the typed arities carry, and is therefore
    ///     configured through the boxed argument list instead. Generated domain actions reach seven
    ///     parameters, and there is no honest maximum to build arities up to.
    /// </summary>
    public required bool IsWide { get; init; }

    /// <summary>The type arguments when the method is generic, e.g. <c>T</c>; empty otherwise.</summary>
    public required EquatableArray<string> TypeParameters { get; init; }

    /// <summary>
    ///     Whether the interface declares this member <c>static abstract</c>. AWS's
    ///     <c>IAmazonService</c> does, for two factory methods.
    /// </summary>
    /// <remarks>
    ///     A static member belongs to the type, not the instance, so there is nothing per-mock to
    ///     record or configure — but a concrete class implementing the interface must still provide
    ///     it. It is implemented as an explicit static member that throws, because unlike an
    ///     instance member returning default, a test can never correct this one by configuring it:
    ///     silence would be a promise the mock cannot keep.
    /// </remarks>
    public required bool IsStatic { get; init; }

    /// <summary>
    ///     The interface that <b>declares</b> this member — not necessarily the one being mocked.
    ///     An explicit implementation must be qualified with it.
    /// </summary>
    public required string DeclaringInterface { get; init; }

    /// <summary>
    ///     An expression seeding the member's result when the test configures nothing, or null to
    ///     leave it at <c>default</c>.
    /// </summary>
    /// <remarks>
    ///     It exists for one case, and that case is everywhere: a method returning <c>Task</c> whose
    ///     default is <see langword="null"/>, so an unconfigured call makes the caller's
    ///     <c>await</c> throw a NullReferenceException far from the mock. <c>ValueTask</c> needs
    ///     nothing — its default is already a completed one.
    /// </remarks>
    public required string? DefaultResult { get; init; }
}
