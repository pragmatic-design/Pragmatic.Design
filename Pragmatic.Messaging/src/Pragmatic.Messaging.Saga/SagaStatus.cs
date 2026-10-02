namespace Pragmatic.Messaging.Saga;

/// <summary>
///     Overall status of a saga instance.
/// </summary>
public enum SagaStatus
{
    /// <summary>Saga is actively processing steps.</summary>
    Active = 0,

    /// <summary>All steps completed successfully.</summary>
    Completed = 1,

    /// <summary>Saga failed and compensation ran.</summary>
    Compensated = 2,

    /// <summary>Saga timed out.</summary>
    TimedOut = 3,

    /// <summary>Saga failed and compensation also failed.</summary>
    Faulted = 4
}
