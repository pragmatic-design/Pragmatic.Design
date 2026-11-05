using Pragmatic.Persistence.Sql.Attributes;

namespace Pragmatic.Messaging.Engines.Sql;

/// <summary>
///     A saga instance of an <c>[EnableSagaPersistence]</c> boundary the Sql engine serves (<c>__SagaInstances</c>), read and written by
///     <see cref="SqlSagaRepository{TSaga, TState}" />. The table is EF Core's (<c>SagaEntityTypeConfiguration</c>), column for column, so a
///     database holds one <c>__SagaInstances</c> whichever engine writes it; its indexes are added by the generator, as EF Core's.
/// </summary>
[Entity]
[Table("__SagaInstances")]
public sealed class SagaInstanceRow : ISagaInstanceRecord
{
    public Guid Id { get; set; }

    [MaxLength(512)]
    public string SagaType { get; set; } = "";

    [MaxLength(128)]
    public string CorrelationId { get; set; } = "";

    public int State { get; set; }

    public string? StateData { get; set; }

    /// <summary>The <c>SagaStatus</c>, by number.</summary>
    public int Status { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? LastStepAt { get; set; }

    public DateTimeOffset? TimeoutAt { get; set; }

    [MaxLength(2048)]
    public string? LastError { get; set; }

    /// <summary>The tenant the saga belongs to; empty in a single-tenant application.</summary>
    [MaxLength(128)]
    public string TenantId { get; set; } = "";

    /// <summary>The optimistic concurrency token: raised on every save, compared in the update's condition.</summary>
    public int Version { get; set; }
}
