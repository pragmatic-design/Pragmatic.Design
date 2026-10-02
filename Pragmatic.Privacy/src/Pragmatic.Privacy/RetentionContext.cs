namespace Pragmatic.Privacy;

/// <summary>
///     What is known about a record when deciding how long it may be kept.
/// </summary>
/// <param name="SubjectRef">The subject's opaque reference.</param>
/// <param name="EntityType">The type holding the data, e.g. <c>Order</c>.</param>
/// <param name="Purpose">
///     Why the record is held. This, not the type, is what determines the period.
/// </param>
/// <param name="CreatedAt">When the record came into being — the point the clock runs from.</param>
/// <param name="DeclaredDefault">
///     The default declared on the classification, when there is one. A starting point, never the answer:
///     the attribute cannot know the lawful basis of an individual row.
/// </param>
public readonly record struct RetentionContext(
    string SubjectRef,
    string EntityType,
    string Purpose,
    DateTimeOffset CreatedAt,
    TimeSpan? DeclaredDefault = null);
