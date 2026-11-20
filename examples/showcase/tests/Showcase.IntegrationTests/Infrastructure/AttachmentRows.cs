using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Showcase.Booking;
using Showcase.Booking.Entities;

namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     An attachment row by its key, deleted or not, and the one change the retention tests make to it.
/// </summary>
/// <remarks>
///     Through the boundary's repository with the query filters lifted, and its unit of work: every engine registers the repository of a
///     trait's entity, EF Core included (#888), so the suite has one copy on all three.
/// </remarks>
public static class AttachmentRows
{
    /// <summary>The tenant the suite's HTTP clients write under: outside a request there is none.</summary>
    private const string Tenant = "test-tenant";

    /// <summary>The row, past every filter; null when there is none.</summary>
    /// <remarks>
    ///     Soft delete is lifted twice: by the caller and by its name. EF Core's named query filter answers the second, and the engines decide
    ///     it the same way: a row comes back only when both are lifted (<c>PragmaticFilterDecision</c>).
    /// </remarks>
    public static async Task<ReservationAttachment?> FindAsync(IServiceProvider services, Guid attachmentId)
    {
        using var tenant = TenantScope.BeginScope(Tenant);
        using var scope = services.CreateScope();
        var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();
        using var all = filters.DisableAll();
        using var softDelete = filters.DisableQueryFilter("SoftDelete");
        return await scope.ServiceProvider.GetRequiredService<IReadRepository<ReservationAttachment>>().GetByIdAsync(attachmentId);
    }

    /// <summary>Moves a soft-deleted row's deletion back to <paramref name="deletedAt" />.</summary>
    public static async Task BackdateTheDeletionAsync(IServiceProvider services, Guid attachmentId, DateTimeOffset deletedAt)
    {
        using var tenant = TenantScope.BeginScope(Tenant);
        using var scope = services.CreateScope();
        var filters = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>();
        using var all = filters.DisableAll();
        using var softDelete = filters.DisableQueryFilter("SoftDelete");
        var attachments = scope.ServiceProvider.GetRequiredService<IRepository<ReservationAttachment>>();
        var row = await attachments.GetByIdAsync(attachmentId) ?? throw new InvalidOperationException($"no attachment {attachmentId}");
        row.DeletedAt = deletedAt;
        attachments.Update(row);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary)).SaveChangesAsync();
    }
}
