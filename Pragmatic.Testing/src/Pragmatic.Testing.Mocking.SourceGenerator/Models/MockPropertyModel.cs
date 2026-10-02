using Pragmatic.SourceGen;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Models;

/// <summary>A property to expose on the generated mock.</summary>
internal sealed record MockPropertyModel
{
    /// <summary>The property name, as declared on the mocked type.</summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The name of the public member exposing it. Same as <see cref="Name"/> for an interface,
    ///     where explicit implementation leaves the name free; suffixed with <c>Setup</c> when the
    ///     mocked type is a class, because there the <c>override</c> holds the name itself.
    /// </summary>
    public required string MemberName { get; init; }

    /// <summary>The fully-qualified property type, <c>global::</c>-prefixed.</summary>
    public required string Type { get; init; }

    /// <summary>Whether the interface declares a getter.</summary>
    public required bool HasGetter { get; init; }

    /// <summary>Whether the interface declares a setter.</summary>
    public required bool HasSetter { get; init; }

    /// <summary>
    ///     The interface that <b>declares</b> this member — not necessarily the one being mocked.
    ///     An explicit implementation must be qualified with it.
    /// </summary>
    public required string DeclaringInterface { get; init; }

    /// <summary>
    ///     The parameters when this is an indexer; empty for an ordinary property. An indexer has no
    ///     name to expose a configurable member under, so it is implemented and returns default.
    /// </summary>
    public required EquatableArray<MockParameterModel> IndexerParameters { get; init; }

    /// <summary>
    ///     Whether the interface declares this property <c>static abstract</c>. See
    ///     <see cref="MockMethodModel.IsStatic"/> — same reasoning, same treatment.
    /// </summary>
    public required bool IsStatic { get; init; }
}
