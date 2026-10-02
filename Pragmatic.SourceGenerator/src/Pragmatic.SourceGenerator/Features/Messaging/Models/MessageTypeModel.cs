namespace Pragmatic.SourceGenerator.Features.Messaging.Models;

/// <summary>
///     Model for a message type the registry has to resolve: one consumed by an
///     <c>IMessageHandler&lt;T&gt;</c> of this assembly, or a domain event this assembly declares and
///     can therefore publish. Used to generate the AOT-safe MessageTypeRegistry.
/// </summary>
internal sealed record MessageTypeModel
{
    /// <summary>Fully qualified name of the message type.</summary>
    public required string Fqn { get; init; }

    /// <summary>Short name (for comments/display).</summary>
    public required string ShortName { get; init; }

    /// <summary>
    ///     The assembly that declares the type, when it came from a declared domain event.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It is what names the generated registration's namespace in an assembly with <b>no</b>
    ///     handlers: the namespace is per-assembly because the class name is fixed, and two
    ///     publisher-only assemblies referenced by one host would otherwise collide on CS0433. Empty
    ///     for a type that came from a handler, where the handler model carries the name instead.
    /// </remarks>
    public string AssemblyName { get; init; } = "";
}
