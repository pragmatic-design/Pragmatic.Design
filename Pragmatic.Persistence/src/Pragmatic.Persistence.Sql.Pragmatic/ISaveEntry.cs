using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Sql.Pragmatic;

/// <summary>What the save's phases read and change of a tracked entity, whatever engine tracks it.</summary>
internal interface ISaveEntry
{
    object Entity { get; }

    bool IsAdded { get; }

    bool IsModified { get; }

    bool IsDeleted { get; }

    /// <summary>A modified <see cref="ISoftDelete" /> whose flag goes from false to true: a delete, for the lifecycle events and the audit log.</summary>
    bool IsSoftDeleting { get; }

    /// <summary>The tenant the entity was read with (an <see cref="ITenantEntity" />).</summary>
    string? OriginalTenantId { get; }

    /// <summary>A deleted entity saved as a modified one: a soft delete.</summary>
    void DeleteAsModified();
}
