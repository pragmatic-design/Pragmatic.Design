namespace TimeOff.Leave.Dtos;

/// <summary>
///     What an erasure did: how many records it cleared, what it kept and why, whether the employee's
///     reference still leads to anyone, and whether their key was destroyed.
/// </summary>
/// <remarks>
///     <c>KeyDestroyed</c> is the answer for the one field erased by crypto-shredding
///     (<c>LeaveRequest.Reason</c>): the column is not cleared, the key that opens it is destroyed, and
///     the two are different claims. The outcome has carried it all along and nothing showed it — which
///     is how an erasure that destroyed no key could look complete.
/// </remarks>
public sealed record ErasureDto(
    int ErasedCount, IReadOnlyList<RetainedItem> Retained, bool IdentityForgotten, bool IsTotal, bool KeyDestroyed)
{
    internal static ErasureDto From(ErasureOutcome outcome) =>
        new(outcome.ErasedCount, outcome.Retained, outcome.IdentityForgotten, outcome.IsTotal, outcome.KeyDestroyed);
}
