namespace Pragmatic.Events.Attributes;

/// <summary>
///     Marks a class as a domain event handler for source generator discovery.
///     The class must implement <see cref="IDomainEventHandler{TEvent}" />.
/// </summary>
/// <remarks>
///     The Composition source generator discovers classes with this attribute
///     and generates registration in <c>AddPragmaticEventHandlers()</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventHandlerAttribute : Attribute;
