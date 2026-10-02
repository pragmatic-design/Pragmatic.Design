using Pragmatic.Persistence.Lifecycle;

namespace Showcase.Booking.Infrastructure.Lifecycle;

/// <summary>
/// Creates a default RoomAssignment when a Reservation is created.
/// Demonstrates [HasPresets] + [PresetProvider] pipeline integration.
/// </summary>
public sealed class ReservationPresetProvider : IPresetProvider<Reservation>
{
    public Task<IReadOnlyList<object>> CreatePresetsAsync(
        Reservation parent, LifecycleContext context, CancellationToken ct)
    {
        var presets = new List<object>
        {
            RoomAssignment.Create(parent.Id, parent.GuestId, "TBD", DateTimeOffset.UtcNow)
        };

        return Task.FromResult<IReadOnlyList<object>>(presets);
    }
}
