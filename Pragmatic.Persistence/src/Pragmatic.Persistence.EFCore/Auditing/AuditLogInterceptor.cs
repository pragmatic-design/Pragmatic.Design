using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Auditing;

/// <summary>
///     EF Core interceptor that writes an append-only <see cref="AuditEntry"/> for every inserted,
///     updated or deleted <see cref="IAuditedEntity"/> — into the context being saved, so the audit
///     row and the change it describes are one transaction by construction rather than by arrangement.
///     Captures the current user (when an <see cref="ICurrentUser"/> is available), a testable
///     timestamp, and the ambient trace id.
/// </summary>
/// <remarks>
///     <para>
///         A soft delete (Modified entry whose <c>IsDeleted</c> flag flips to true) is recorded as
///         <c>"Deleted"</c>, and a restore as <c>"Restored"</c> — the audit trail reflects the domain
///         semantics, not the EF change-tracker state.
///     </para>
///     <para>
///         <b>Store-generated keys:</b> <c>EntityId</c> is read at <c>SavingChanges</c> time. For
///         client-assigned keys (the framework default — the generated entity assigns its
///         <c>PersistenceId</c> in its constructor) the value is final. For database-generated keys (e.g. int
///         identity) an <b>Added</b> entity still carries EF's temporary value here; such entries record
///         <c>EntityId = ""</c> rather than a misleading temporary number.
///     </para>
/// </remarks>
public sealed class AuditLogInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser? _currentUser;
    private readonly AuditEntryPreparer _preparer;

    /// <summary>Creates the interceptor using the system clock and no user.</summary>
    public AuditLogInterceptor() : this(TimeProvider.System, null) { }

    /// <summary>Creates the interceptor with a time provider and optional current user.</summary>
    public AuditLogInterceptor(TimeProvider timeProvider, ICurrentUser? currentUser)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _currentUser = currentUser;
        _preparer = new AuditEntryPreparer(
            new PatternAuditDetailRedactor(), new HourlyAuditSegmentNaming(), _timeProvider);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        WriteAuditEntries(eventData);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        WriteAuditEntries(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditEntries(DbContextEventData eventData)
    {
        if (eventData.Context is not { } context)
            return;

        // Snapshot first — adding audit rows mutates the change tracker mid-enumeration.
        var audited = Interceptors.SaveEntries.Of<IAuditedEntity>(eventData)
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (audited.Count == 0)
            return;

        var now = _timeProvider.GetUtcNow();

        foreach (var entry in audited)
        {
            var operation = entry.State switch
            {
                EntityState.Added => AuditedChange.EntityCreated,
                EntityState.Deleted => AuditedChange.EntityDeleted,
                _ when Interceptors.SoftDeleteDetection.IsSoftDeleting(entry) => AuditedChange.EntityDeleted,
                _ when Interceptors.SoftDeleteDetection.IsSoftRestoring(entry) => AuditedChange.EntityRestored,
                _ => AuditedChange.EntityUpdated
            };

            AuditEntryStaging.Stage(
                context,
                AuditedChange.Entry(operation, entry.Entity.GetType().Name, GetPrimaryKey(entry), now, _currentUser),
                _preparer);
        }
    }

    private static string GetPrimaryKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return "";

        // A temporary value (store-generated key not yet assigned) would record a misleading
        // number — prefer an empty id over a wrong one. See the class remarks.
        var parts = new string[key.Properties.Count];
        for (var i = 0; i < key.Properties.Count; i++)
        {
            var property = entry.Property(key.Properties[i].Name);
            if (property.IsTemporary)
                return "";
            parts[i] = property.CurrentValue?.ToString() ?? "";
        }

        return string.Join(",", parts);
    }
}
