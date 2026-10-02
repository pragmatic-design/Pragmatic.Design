namespace Pragmatic.Privacy;

/// <summary>
///     How long a record may be kept, and on what grounds.
/// </summary>
/// <param name="KeepUntil">
///     When the record becomes eligible for deletion. <see langword="null" /> means indefinitely — which
///     is only ever correct alongside a stated obligation.
/// </param>
/// <param name="Basis">
///     Why. Carried into the processing register and into the answer a subject is given, so it has to
///     read as a reason and not as a code.
/// </param>
public readonly record struct RetentionDecision(DateTimeOffset? KeepUntil, string Basis)
{
    /// <summary>Keep for a fixed period from the record's creation.</summary>
    public static RetentionDecision For(RetentionContext context, TimeSpan period, string basis)
        => new(context.CreatedAt.Add(period), basis);

    /// <summary>
    ///     Keep with no end date, under a stated obligation.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     No basis given. Indefinite retention without a reason is indistinguishable from having
    ///     forgotten to delete, and the register cannot tell the two apart.
    /// </exception>
    public static RetentionDecision Indefinite(string basis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        return new RetentionDecision(null, basis);
    }

    /// <summary>Nothing requires the record to be kept; it may go now.</summary>
    public static RetentionDecision NoLongerNeeded(string basis) => new(DateTimeOffset.MinValue, basis);

    /// <summary>Whether the record may be deleted at <paramref name="now" />.</summary>
    public bool IsExpired(DateTimeOffset now) => KeepUntil is { } until && now >= until;
}
