using Pragmatic.MultiTenancy;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     EF Core entity for saga instance persistence.
///     Ad-hoc table — not a Pragmatic entity (no repository/query generation).
/// </summary>
/// <remarks>
///     Implements <see cref="ITenantEntity"/> for row-level tenant isolation: the repository stamps
///     <see cref="TenantId"/> from the ambient tenant on create, the unique index keys on it so two
///     tenants can run the same (SagaType, CorrelationId), and (multi-tenant hosts only) the generated
///     boundary DbContext adds a fail-closed EF query filter. Single-tenant hosts leave it empty and
///     no filter is emitted, so behaviour is unchanged.
/// </remarks>
public sealed class SagaInstance : ITenantEntity
{
    /// <summary>Maximum persisted length of <see cref="CorrelationId"/> (mirrors the column mapping).</summary>
    public const int MaxCorrelationIdLength = 128;

    /// <summary>
    ///     Guards a saga correlation id against the persisted column limit, turning a raw provider
    ///     truncation/insert error into a clear, actionable exception at the repository boundary.
    /// </summary>
    /// <exception cref="ArgumentException">The correlation id exceeds <see cref="MaxCorrelationIdLength"/>.</exception>
    public static void EnsureValidCorrelationId(string correlationId)
    {
        if (correlationId is not null && correlationId.Length > MaxCorrelationIdLength)
            throw new ArgumentException(
                $"Saga CorrelationId is {correlationId.Length} characters; the maximum is {MaxCorrelationIdLength}. " +
                "Use a shorter correlation key (e.g. a hash or the entity id).",
                nameof(correlationId));
    }

    /// <summary>Unique saga instance ID. Auto-assigned on construction to prevent Guid.Empty persistence.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FQN of the saga type.</summary>
    public required string SagaType { get; set; }

    /// <summary>Correlation ID linking related messages.</summary>
    public required string CorrelationId { get; set; }

    /// <summary>
    ///     Last state (enum value) the saga reached. This is NOT a terminal marker — on timeout/fault the
    ///     saga's <see cref="Status"/> becomes terminal but <see cref="State"/> keeps the last step reached
    ///     (there is no universal terminal value for a user-defined state enum). Treat <see cref="Status"/>
    ///     as the authority for "is this saga still running", not <see cref="State"/>.
    /// </summary>
    public int State { get; set; }

    /// <summary>Serialized saga-specific data (JSON).</summary>
    public string? StateData { get; set; }

    /// <summary>Overall saga status.</summary>
    public SagaStatus Status { get; set; } = SagaStatus.Active;

    /// <summary>When the saga started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the saga completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Timestamp of the last step execution.</summary>
    public DateTimeOffset? LastStepAt { get; set; }

    /// <summary>
    ///     Deadline after which the current step is considered timed out.
    ///     Set by the orchestrator on steps decorated with <c>[SagaTimeout]</c>,
    ///     cleared on transition to a step without a timeout.
    /// </summary>
    public DateTimeOffset? TimeoutAt { get; set; }

    /// <summary>Last error message.</summary>
    public string? LastError { get; set; }

    /// <summary>
    ///     Tenant this saga belongs to. Stamped from the ambient tenant on create by the repository;
    ///     empty in single-tenant hosts. Part of the active-saga unique index so the same
    ///     (SagaType, CorrelationId) can run concurrently for different tenants.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    ///     Optimistic-concurrency token. Bumped on every save and checked in the UPDATE's WHERE clause
    ///     so two messages racing on the same saga can't silently overwrite each other's transition —
    ///     the loser gets a <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> and
    ///     its message is redelivered against fresh state. Provider-agnostic (no native rowversion needed).
    /// </summary>
    public int Version { get; set; }

    /// <summary>Navigation to saga steps.</summary>
    public List<SagaStep> Steps { get; set; } = [];
}
