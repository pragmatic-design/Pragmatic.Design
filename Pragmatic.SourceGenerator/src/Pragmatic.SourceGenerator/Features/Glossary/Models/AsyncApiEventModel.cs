using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Glossary.Models;

/// <summary>
///     One event in the generated AsyncAPI document: a domain event type (implements
///     <c>IDomainEvent</c>), surfaced as an AsyncAPI channel + message.
/// </summary>
internal sealed record AsyncApiEventModel
{
    /// <summary>Event type simple name — the channel/message key when it is unambiguous.</summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Containing namespace, empty for the global namespace. Part of the event's identity: without it
    ///     two homonymous events in different boundaries collapse into one contract.
    /// </summary>
    public string Namespace { get; init; } = "";

    /// <summary>
    ///     Namespace-qualified type name — the event's identity, used to deduplicate and to disambiguate
    ///     the channel/message key when two events share a simple name.
    /// </summary>
    public string FullName => Namespace.Length == 0 ? Name : Namespace + "." + Name;

    /// <summary>
    ///     True when the event is a public integration event (implements IIntegrationEvent or has
    ///     [PublicEvent]) — the cross-boundary contract — vs an internal domain event.
    /// </summary>
    public bool IsPublic { get; init; }

    /// <summary>True when the event has [ObsoleteEvent] — surfaced as x-pragmatic-obsolete.</summary>
    public bool IsObsolete { get; init; }

    /// <summary>The event's payload properties (name + JSON type) — the concrete contract.</summary>
    public EquatableArray<AsyncApiPropertyModel> Properties { get; init; } = EquatableArray<AsyncApiPropertyModel>.Empty;
}
