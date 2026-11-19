using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Booking.Entities;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     An attachment row by its key, deleted or not, and the one change the retention tests make to it.
/// </summary>
/// <remarks>
///     EF Core's copy, through the boundary's <c>DbContext</c>: EF Core registers no repository for a trait's entity (#888). The engine suites
///     replace this file with their own, through the repository they do register (<c>Showcase.Sql.IntegrationTests/AttachmentRows.cs</c>).
/// </remarks>
public static class AttachmentRows
{
    /// <summary>The row, past every filter; null when there is none.</summary>
    public static async Task<ReservationAttachment?> FindAsync(IServiceProvider services, Guid attachmentId)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BookingDbContext>().Set<ReservationAttachment>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == attachmentId);
    }

    /// <summary>Moves a soft-deleted row's deletion back to <paramref name="deletedAt" />.</summary>
    public static async Task BackdateTheDeletionAsync(IServiceProvider services, Guid attachmentId, DateTimeOffset deletedAt)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
        var row = await db.Set<ReservationAttachment>().IgnoreQueryFilters().SingleAsync(a => a.Id == attachmentId);
        row.DeletedAt = deletedAt;
        await db.SaveChangesAsync();
    }
}
