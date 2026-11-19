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
///     The engines' copy, linked into the documents suite too: through the boundary's repository with the query filters lifted, and its unit
///     of work. It replaces the suite's <c>Infrastructure/AttachmentRows.cs</c>, which reads through EF Core's <c>DbContext</c> because EF Core
///     registers no repository for a trait's entity (#888); when it does, this is the only copy.
/// </remarks>
public static class AttachmentRows
{
    /// <summary>The tenant the suite's HTTP clients write under: outside a request there is none.</summary>
    private const string Tenant = "test-tenant";

    /// <summary>The row, past every filter; null when there is none.</summary>
    /// <remarks>
    ///     Soft delete is lifted twice, as on EF Core: by the caller and by its name. The engines decide it the same way, and a row comes back
    ///     only when both are lifted (<c>PragmaticFilterDecision</c>).
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
