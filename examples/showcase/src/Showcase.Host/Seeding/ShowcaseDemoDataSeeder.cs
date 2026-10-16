using Microsoft.Extensions.Options;
using Pragmatic.MultiTenancy;
using Pragmatic.Pipeline;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.Host.Configuration;

namespace Showcase.Host.Seeding;

/// <summary>
///     Puts three hotels, with their room types, into the tenant <see cref="DemoTenant" /> when the host
///     starts in Development with <c>Showcase:SeedDemoData</c> on, so a reader can search the catalogue
///     right after <c>dotnet run</c>.
/// </summary>
/// <remarks>
///     <para>
///         Through the generated catalogue actions, as a client's request would write them, and not by
///         inserting rows: the same validation, defaults and audit run. A hosted service has no request and
///         therefore no principal, so it enters an internal call, the framework's declared road for a caller
///         that answers for itself (a job, a message handler); the generated <c>ICatalogActions</c> says so.
///     </para>
///     <para>
///         <see cref="Property" /> is a tenant's: the writes run inside <c>TenantScope.BeginScope</c> and a
///         container scope of their own, so <c>TenantInterceptor</c> stamps them as that tenant's, and the
///         existence check reads only that tenant's rows.
///     </para>
///     <para>
///         Idempotent by code: a hotel whose code is already there is left alone, so a restart adds nothing.
///         Development only, because seeding a production database on start is a decision nobody should
///         make by leaving a flag on.
///     </para>
/// </remarks>
public sealed partial class ShowcaseDemoDataSeeder(
    IOptions<ShowcaseOptions> options,
    IHostEnvironment environment,
    IServiceScopeFactory scopes,
    ILogger<ShowcaseDemoDataSeeder> logger) : IHostedService
{
    /// <summary>The tenant the demo data belongs to: send <c>X-Tenant-Id: demo</c> to read it.</summary>
    public const string DemoTenant = "demo";

    private static readonly (string Code, string Name, string City, string Country, int Stars, string TimeZone,
        (string Code, string Name, int MaxOccupancy, decimal BaseRate, int TotalRooms)[] Rooms)[] Hotels =
    [
        ("DEMO-ROME", "Hotel Colosseo", "Rome", "Italy", 4, "Europe/Rome",
            [("STD", "Standard Double", 2, 120m, 20), ("SUI", "Junior Suite", 3, 260m, 5)]),
        ("DEMO-MILAN", "Navigli Boutique", "Milan", "Italy", 3, "Europe/Rome",
            [("STD", "Standard Double", 2, 95m, 12)]),
        ("DEMO-PARIS", "Grand Hôtel Opéra", "Paris", "France", 5, "Europe/Paris",
            [("DLX", "Deluxe King", 2, 340m, 15), ("FAM", "Family Room", 4, 410m, 6)]),
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        if (!options.Value.SeedDemoData || !environment.IsDevelopment())
            return;

        using var tenant = TenantScope.BeginScope(DemoTenant);
        var scope = scopes.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var services = scope.ServiceProvider;
            var existing = services.GetRequiredService<IReadRepository<Property>>();
            var catalog = services.GetRequiredService<ICatalogActions>();
            using var internalCall = services.GetRequiredService<ICallContext>().EnterInternalCall();

            var created = 0;
            foreach (var hotel in Hotels)
            {
                if (await existing.ExistsAsync(PropertySpecifications.ByCode(hotel.Code), ct).ConfigureAwait(false))
                    continue;

                var property = await catalog.Properties
                    .CreateProperty(
                        code: hotel.Code, name: hotel.Name, city: hotel.City, country: hotel.Country,
                        starRating: hotel.Stars, timeZone: hotel.TimeZone, ct: ct)
                    .ConfigureAwait(false);
                if (property.IsFailure)
                    throw new InvalidOperationException($"Demo hotel {hotel.Code} was refused: {property.Error}");

                foreach (var room in hotel.Rooms)
                {
                    var roomType = await catalog.RoomTypes
                        .CreateRoomType(
                            propertyId: property.Value.PersistenceId, name: room.Name, code: room.Code,
                            baseRate: room.BaseRate, maxOccupancy: room.MaxOccupancy, currency: "EUR",
                            totalRooms: room.TotalRooms, ct: ct)
                        .ConfigureAwait(false);
                    if (roomType.IsFailure)
                        throw new InvalidOperationException(
                            $"Room type {room.Code} of demo hotel {hotel.Code} was refused: {roomType.Error}");
                }

                created++;
            }

            LogSeeded(created, Hotels.Length - created, DemoTenant);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Demo data: {Created} hotel(s) created, {Present} already there, in tenant {Tenant}.")]
    private partial void LogSeeded(int created, int present, string tenant);
}
