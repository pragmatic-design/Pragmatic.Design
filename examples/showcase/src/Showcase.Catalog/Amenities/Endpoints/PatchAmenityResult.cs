namespace Showcase.Catalog.Amenities.Endpoints;

/// <summary>
///     What a PATCH changed: the amenity and the names of the properties the patch set.
/// </summary>
/// <remarks>
///     ⚠️ Not <c>ModifiedProperties</c>: that name is reserved for the framework's own change tracking and is
///     stripped from every object on the wire (<c>ReservedWireNames</c>), so a member called that never
///     reached the client.
/// </remarks>
public sealed record PatchAmenityResult(Guid Id, IReadOnlyList<string> ChangedProperties);
