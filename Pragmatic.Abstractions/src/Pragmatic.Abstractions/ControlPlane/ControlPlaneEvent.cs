using System.Text.Json.Serialization;

namespace Pragmatic.ControlPlane;

/// <summary>
///     Base for events streamed from the control plane to connected hosts.
/// </summary>
/// <param name="SourceHostId">Identifier of the host (or actor) that originated the event.</param>
/// <param name="Timestamp">When the event occurred.</param>
/// <remarks>
///     <para>
///         Subclasses represent the concrete event payloads (config change, host state). The <c>$type</c>
///         discriminator is declared here rather than left to each transport: a transport that invented its
///         own shape could write an event it was then unable to read back, which is precisely what happened
///         to the Agent broadcast path. Mirrors <see cref="HostCommand" />.
///     </para>
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ConfigChangedEvent), nameof(ConfigChangedEvent))]
[JsonDerivedType(typeof(HostStateChangedEvent), nameof(HostStateChangedEvent))]
public abstract record ControlPlaneEvent(string SourceHostId, DateTimeOffset Timestamp);
