using Pragmatic.SourceGen;

namespace Pragmatic.Testing.Mocking.SourceGenerator.Models;

/// <summary>One <c>[GenerateMock&lt;T&gt;]</c> declaration, resolved into everything the template needs.</summary>
internal sealed record MockModel
{
    /// <summary>The fully-qualified interface type, <c>global::</c>-prefixed.</summary>
    public required string InterfaceFullName { get; init; }

    /// <summary>The interface's simple name, used in member display names — e.g. <c>IClock</c>.</summary>
    public required string InterfaceShortName { get; init; }

    /// <summary>The generated class name — <c>IClock</c> becomes <c>ClockMock</c> unless overridden.</summary>
    public required string ClassName { get; init; }

    /// <summary>The properties to expose.</summary>
    public required EquatableArray<MockPropertyModel> Properties { get; init; }

    /// <summary>The methods to implement.</summary>
    public required EquatableArray<MockMethodModel> Methods { get; init; }

    /// <summary>
    ///     The events to implement. They get empty accessors and nothing else: an interface declaring
    ///     one cannot be implemented without them, and no test in this repository raises a mocked
    ///     event — so the alternative to empty accessors is a type that does not compile.
    /// </summary>
    public required EquatableArray<MockEventModel> Events { get; init; }

    /// <summary>
    ///     Whether the mocked type is a class the mock <b>derives</b> from, rather than an interface
    ///     it implements. The Azure and Google Cloud SDKs ship clients as classes with virtual
    ///     members and no interface, which is the only reason this mode exists.
    /// </summary>
    /// <remarks>
    ///     It changes what can be mocked and what it is called. Only virtual members can be
    ///     intercepted, and an <c>override</c> occupies the member's own name — so the configurable
    ///     member cannot share it, and takes a <c>Setup</c> suffix. Interfaces keep the plain name,
    ///     which explicit implementation leaves free.
    /// </remarks>
    public required bool IsClass { get; init; }
}
