namespace Pragmatic.Maintenance;

/// <summary>
///     Immutable progress event emitted during database initialization.
/// </summary>
/// <param name="Phase">Current phase (e.g. "migration", "seeding", "complete").</param>
/// <param name="Message">Human-readable description of the current step.</param>
/// <param name="ProgressPercent">Optional 0-100 progress indicator.</param>
/// <param name="DatabaseName">Name of the database being processed, if applicable.</param>
/// <param name="IsError">Whether this event represents an error.</param>
/// <param name="ErrorDetail">Error detail string (typically exception.ToString()).</param>
/// <param name="Timestamp">
///     When this event was created. Defaults to <see cref="DateTimeOffset.UtcNow"/> when not
///     supplied. Pass an explicit value when replaying or batching events so the timestamp
///     reflects the actual occurrence time rather than the construction time.
/// </param>
public sealed record MigrationProgressEvent(
    string Phase,
    string Message,
    double? ProgressPercent = null,
    string? DatabaseName = null,
    bool IsError = false,
    string? ErrorDetail = null,
    DateTimeOffset? Timestamp = null)
{
    // A property *initializer* runs once, at construction. It cannot police an object initializer
    // or a `with` expression: those assign through the `init` accessor afterwards, and the record
    // copy constructor copies the backing field directly. So the rule lives in the accessor, and
    // the initializer calls the same function — that is the only shape that covers every path.
    /// <summary>
    ///     When this event was created. Always non-null at runtime; the nullable type preserves
    ///     the positional-parameter API so callers can omit it.
    /// </summary>
    public DateTimeOffset? Timestamp
    {
        get;
        init => field = value ?? DateTimeOffset.UtcNow;
    } = Timestamp ?? DateTimeOffset.UtcNow;

    /// <summary>
    ///     Optional 0-100 progress indicator. <c>null</c> when the phase has no measurable
    ///     progress (e.g. an indeterminate step).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when set to a non-null value outside the inclusive range [0, 100] — on every
    ///     path, constructor, object initializer and <c>with</c> alike.
    /// </exception>
    public double? ProgressPercent
    {
        get;
        init => field = ValidateProgressPercent(value);
    } = ValidateProgressPercent(ProgressPercent);

    /// <summary>
    ///     Range check for <see cref="ProgressPercent" />. <c>NaN</c> is rejected explicitly:
    ///     every relational comparison against it is false, so a <c>&lt; 0 or &gt; 100</c> pattern
    ///     would let it through.
    /// </summary>
    private static double? ValidateProgressPercent(double? value)
    {
        if (value is null)
            return null;

        if (double.IsNaN(value.Value) || value.Value is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(ProgressPercent), value,
                "ProgressPercent must be null or within the inclusive range [0, 100].");

        return value;
    }

    /// <summary>
    ///     Creates a terminal failure event (<see cref="IsError"/> = <c>true</c>) carrying the
    ///     error detail. Use together with <see cref="IMigrationProgressStream.ReportFailure"/> to
    ///     signal that initialization/migration aborted.
    /// </summary>
    /// <param name="message">Human-readable failure summary.</param>
    /// <param name="exception">Optional exception; its <c>ToString()</c> populates <see cref="ErrorDetail"/>.</param>
    /// <param name="phase">Phase label (defaults to <c>"error"</c>).</param>
    /// <param name="databaseName">Database being processed, if applicable.</param>
    public static MigrationProgressEvent Failed(
        string message,
        Exception? exception = null,
        string phase = "error",
        string? databaseName = null)
        => new(
            phase,
            message,
            IsError: true,
            ErrorDetail: exception?.ToString(),
            DatabaseName: databaseName);
}
