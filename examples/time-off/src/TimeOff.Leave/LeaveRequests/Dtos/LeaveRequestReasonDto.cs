namespace TimeOff.Leave.Dtos;

/// <summary>
///     What happened when the reason was read, and the text when there is one.
/// </summary>
/// <remarks>
///     ⚠️ The outcome is the answer, and the text is the exception. A DTO with only a nullable string
///     would collapse three different facts — "no reason was given", "the reason is there" and "the
///     employee was erased" — into one empty field, and the first and the last would be
///     indistinguishable. That is the whole reason this read is not a projection.
/// </remarks>
public sealed class LeaveRequestReasonDto
{
    /// <summary>One of <see cref="LeaveRequestReason" />, as its name.</summary>
    public required string Outcome { get; init; }

    /// <summary>The reason, when the outcome is <c>Given</c>; null otherwise.</summary>
    public string? Reason { get; init; }
}

/// <summary>What a read of a leave request's reason can say.</summary>
public enum LeaveRequestReason
{
    /// <summary>The employee gave no reason.</summary>
    NotGiven = 0,

    /// <summary>The reason is here.</summary>
    Given = 1,

    /// <summary>
    ///     The employee was erased: the ciphertext is still in the row and nobody can read it, here or
    ///     in any backup taken before the erasure.
    /// </summary>
    Erased = 2,
}
