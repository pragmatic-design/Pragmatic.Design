namespace Pragmatic.ControlPlane;

/// <summary>
///     Progress snapshot for an ongoing migration.
///     Null on <see cref="IHostStatus.MigrationStatus"/> when the host is not migrating.
/// </summary>
/// <param name="DatabaseName">The database this snapshot describes.</param>
/// <param name="TotalChanges">Total schema/data changes planned for this migration.</param>
/// <param name="AppliedChanges">Changes applied so far (0..<paramref name="TotalChanges"/>).</param>
/// <param name="ProgressPercent">Progress as a 0-100 percentage.</param>
/// <param name="IsError">
///     Whether the migration has errored. When <c>true</c>, <paramref name="ErrorMessage"/>
///     MUST be non-empty; when <c>false</c>, <paramref name="ErrorMessage"/> MUST be null —
///     the constructor rejects any other combination. The property of the same name is derived
///     from <paramref name="ErrorMessage"/>, so the pair cannot drift apart afterwards.
/// </param>
/// <param name="ErrorMessage">The error detail when <paramref name="IsError"/> is <c>true</c>; otherwise null.</param>
public sealed record MigrationStatus(
    string DatabaseName,
    int TotalChanges,
    int AppliedChanges,
    double ProgressPercent,
    bool IsError,
    string? ErrorMessage)
{
    /// <summary>
    ///     The error detail when the migration has failed; otherwise <c>null</c>. This is the single
    ///     stored side of the error state — <see cref="IsError" /> is derived from it.
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     Thrown by the constructor when the supplied <c>IsError</c> argument contradicts the message.
    /// </exception>
    public string? ErrorMessage { get; init; } = IsError switch
    {
        true when string.IsNullOrEmpty(ErrorMessage) =>
            throw new ArgumentException("ErrorMessage is required when IsError is true.", nameof(ErrorMessage)),
        false when !string.IsNullOrEmpty(ErrorMessage) =>
            throw new ArgumentException("ErrorMessage must be null when IsError is false.", nameof(ErrorMessage)),
        _ => ErrorMessage,
    };

    /// <summary>
    ///     Whether the migration has errored — <c>true</c> exactly when <see cref="ErrorMessage" />
    ///     is non-empty.
    /// </summary>
    /// <remarks>
    ///     Derived, not stored. The two fields describe one fact, and a settable <c>IsError</c> let a
    ///     <c>with</c> expression or an object initializer split them apart after the constructor had
    ///     checked them — a health report could then claim success while carrying an error message.
    ///     Deriving the flag makes that state unreachable rather than merely validated.
    /// </remarks>
    public bool IsError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Progress as a 0-100 percentage.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when outside the inclusive range [0, 100] — on every path, constructor, object
    ///     initializer and <c>with</c> alike.
    /// </exception>
    public double ProgressPercent
    {
        get;
        init => field = ValidateProgressPercent(value);
    } = ValidateProgressPercent(ProgressPercent);

    /// <summary>
    ///     Range check for <see cref="ProgressPercent" />. <c>NaN</c> is rejected explicitly: every
    ///     relational comparison against it is false, so a <c>&lt; 0 or &gt; 100</c> pattern would
    ///     let it through.
    /// </summary>
    private static double ValidateProgressPercent(double value)
        => double.IsNaN(value) || value is < 0 or > 100
            ? throw new ArgumentOutOfRangeException(nameof(ProgressPercent), value,
                "ProgressPercent must be within the inclusive range [0, 100].")
            : value;
}
