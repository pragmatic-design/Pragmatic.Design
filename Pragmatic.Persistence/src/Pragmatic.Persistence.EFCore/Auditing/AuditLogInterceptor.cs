using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Audit;
using Pragmatic.Audit.EFCore;
using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;
using Pragmatic.Pipeline;

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
        WriteAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        WriteAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditEntries(DbContext? context)
    {
        if (context is null)
            return;

        // Snapshot first — adding audit rows mutates the change tracker mid-enumeration.
        var audited = context.ChangeTracker.Entries<IAuditedEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (audited.Count == 0)
            return;

        var now = _timeProvider.GetUtcNow();
        // ICurrentUser.Id stays the SUBJECT throughout a delegated call — that is what keeps ownership
        // stamps, scope filters and preferences behaving. So reading it alone recorded the person an
        // agent was working for in the field named "who acted", and the agent nowhere at all. In an
        // application with first-class delegation that is the one question a trail is asked.
        var delegation = _currentUser?.Delegation;
        var subjectId = _currentUser?.IdOrNull();
        var actorId = delegation?.ActorId ?? subjectId;
        var onBehalfOf = delegation is null ? null : subjectId;
        var correlationId = Activity.Current?.TraceId.ToString();

        foreach (var entry in audited)
        {
            AuditEntryStaging.Stage(
                context,
                new AuditEntry
                {
                    SegmentId = string.Empty,        // assigned by the preparer
                    OccurredAt = now,
                    Category = AuditCategory.Data,
                    // A constant per shape rather than "Data." + action: an operation assembled at
                    // runtime cannot be found by searching the source.
                    Operation = entry.State switch
                    {
                        EntityState.Added => "Data.EntityCreated",
                        EntityState.Deleted => "Data.EntityDeleted",
                        _ when Interceptors.SoftDeleteDetection.IsSoftDeleting(entry) => "Data.EntityDeleted",
                        _ when Interceptors.SoftDeleteDetection.IsSoftRestoring(entry) => "Data.EntityRestored",
                        _ => "Data.EntityUpdated"
                    },
                    // Already a reference by the time a message or a request reaches persistence: the
                    // identity is pseudonymised where it enters the system, not looked up here.
                    ActorRef = actorId,
                    OnBehalfOfRef = onBehalfOf,
                    CorrelationId = correlationId,
                    // What the application was doing, as opposed to what happened to the row. Read from
                    // the ambient the invokers set rather than from Activity.Current, which is null
                    // wherever no tracing listener is registered.
                    BusinessOperation = OperationScope.Current,
                    TargetType = entry.Entity.GetType().Name,
                    TargetId = GetPrimaryKey(entry),
                    Outcome = AuditOutcome.Success,
                },
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
