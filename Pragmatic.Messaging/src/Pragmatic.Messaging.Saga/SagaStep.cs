using Pragmatic.Messaging.Saga;

namespace Pragmatic.Messaging.Saga;

/// <summary>
///     EF Core entity for individual saga step tracking.
/// </summary>
public sealed class SagaStep
{
    /// <summary>Unique step ID.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to SagaInstance.</summary>
    public Guid SagaInstanceId { get; set; }

    /// <summary>Step name (handler method name or event type).</summary>
    public required string StepName { get; set; }

    /// <summary>Serialized input (JSON).</summary>
    public string? Input { get; set; }

    /// <summary>Serialized output (JSON).</summary>
    public string? Output { get; set; }

    /// <summary>Step status.</summary>
    public SagaStepStatus Status { get; set; } = SagaStepStatus.Pending;

    /// <summary>When the step started.</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the step completed.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; set; }

    /// <summary>Navigation to parent saga.</summary>
    public SagaInstance? SagaInstance { get; set; }
}
