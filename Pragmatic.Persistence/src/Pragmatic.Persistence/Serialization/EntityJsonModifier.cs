using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Serialization;

/// <summary>
///     JSON serialization modifier that excludes infrastructure properties from API responses.
///     Applied automatically by the Pragmatic entry point to keep responses clean.
/// </summary>
/// <remarks>
///     The names come from <c>Pragmatic.Contracts.ReservedWireNames</c>, a file linked into this
///     assembly and into the source generator. A private set here would leave the manifest — built
///     from the DTO's shape — publishing them anyway: the document would announce <c>ownerId</c> on
///     a type the wire never carries it on. This end strips, the other end must not promise, and one
///     list is the only shape in which the two cannot disagree.
/// </remarks>
public static class EntityJsonModifier
{
    /// <summary>
    ///     Excludes infrastructure properties (change tracking, tenant, domain events)
    ///     from JSON serialization. Designed for use as a TypeInfoResolver modifier.
    /// </summary>
    public static void ExcludeInfrastructureProperties(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
            return;

        // Change tracking is stripped on the types that do it. Matching the name on every type would
        // take the member away from an application that has one of its own, in silence.
        var tracksItsChanges = typeof(Entity.IChangeTracking).IsAssignableFrom(typeInfo.Type)
                               || typeof(Pragmatic.Events.IHasDomainEvents).IsAssignableFrom(typeInfo.Type);

        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
        {
            var property = typeInfo.Properties[i];

            var stripped = global::Pragmatic.Contracts.ReservedWireNames.IsAlwaysStripped(property.Name)
                           || (tracksItsChanges
                               && global::Pragmatic.Contracts.ReservedWireNames.IsChangeTracking(property.Name));

            if (stripped)
            {
                typeInfo.Properties.RemoveAt(i);
            }
        }
    }
}
