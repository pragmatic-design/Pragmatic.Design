using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     EF Core interceptor that automatically populates audit fields (CreatedAt, UpdatedAt,
///     CreatedBy, UpdatedBy) on entities implementing <see cref="IAuditable" />.
/// </summary>
/// <remarks>
///     <para>
///         Uses <see cref="TimeProvider" /> for timestamps, making auditing fully testable.
///         Falls back to <see cref="TimeProvider.System" /> if no provider is specified.
///     </para>
///     <para>
///         Optionally accepts an <see cref="ICurrentUser" /> to populate CreatedBy/UpdatedBy fields.
///         When no <see cref="ICurrentUser" /> is provided, those fields are left untouched.
///     </para>
///     <para>
///         Behavior:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>On insert (Added): Sets CreatedAt, UpdatedAt, CreatedBy, and UpdatedBy</description>
///         </item>
///         <item>
///             <description>On update (Modified): Sets only UpdatedAt and UpdatedBy, preserving CreatedAt/CreatedBy</description>
///         </item>
///         <item>
///             <description>
///                 CreatedBy/UpdatedBy are written only when the current user is known. With no
///                 <see cref="ICurrentUser" />, or an anonymous one, both are left at their existing
///                 values — a save from a worker, job or CLI does not erase the attribution.
///                 Timestamps are always written.
///             </description>
///         </item>
///     </list>
/// </remarks>
/// <example>
///     <code>
/// // Register with default system clock
/// services.AddDbContext&lt;AppDbContext&gt;(options =>
///     options.AddInterceptors(new AuditingInterceptor()));
///
/// // Register with custom TimeProvider (e.g., for testing)
/// services.AddDbContext&lt;AppDbContext&gt;(options =>
///     options.AddInterceptors(new AuditingInterceptor(myTimeProvider)));
///
/// // Register with identity support for CreatedBy/UpdatedBy
/// services.AddDbContext&lt;AppDbContext&gt;(options =>
///     options.AddInterceptors(new AuditingInterceptor(myTimeProvider, currentUser)));
/// </code>
/// </example>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser? _currentUser;

    /// <summary>
    ///     Creates a new <see cref="AuditingInterceptor" /> using <see cref="TimeProvider.System" />.
    /// </summary>
    public AuditingInterceptor() : this(TimeProvider.System, null)
    {
    }

    /// <summary>
    ///     Creates a new <see cref="AuditingInterceptor" /> using the specified <see cref="TimeProvider" />.
    /// </summary>
    /// <param name="timeProvider">The time provider for audit timestamps.</param>
    public AuditingInterceptor(TimeProvider timeProvider) : this(timeProvider, null)
    {
    }

    /// <summary>
    ///     Creates a new <see cref="AuditingInterceptor" /> using the specified <see cref="TimeProvider" />
    ///     and optional <see cref="ICurrentUser" /> for populating CreatedBy/UpdatedBy fields.
    /// </summary>
    /// <param name="timeProvider">The time provider for audit timestamps.</param>
    /// <param name="currentUser">
    ///     Optional current user for populating CreatedBy/UpdatedBy.
    ///     When <c>null</c>, those fields are left untouched (backward compatible).
    /// </param>
    public AuditingInterceptor(TimeProvider timeProvider, ICurrentUser? currentUser)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditFields(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditFields(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditFields(DbContext? context)
    {
        if (context is null)
            return;

        var now = _timeProvider.GetUtcNow();

        // Null means "nobody to attribute this to": no ICurrentUser was supplied (worker, job, CLI),
        // or the current user is anonymous. Timestamps are still written — they are facts about the
        // save — but attribution is left alone, because overwriting it with null would erase who
        // actually created or last touched the row rather than record that it is unknown.
        var userId = _currentUser?.IdOrNull();

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    // Also guarded on insert: a caller that populated CreatedBy itself (import,
                    // seeding, a backfill attributing rows to their original author) keeps it.
                    if (userId is not null)
                    {
                        entry.Entity.CreatedBy = userId;
                        entry.Entity.UpdatedBy = userId;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    // CreatedBy is never touched here — it is written once, on insert.
                    if (userId is not null)
                        entry.Entity.UpdatedBy = userId;

                    break;
            }
        }
    }
}
