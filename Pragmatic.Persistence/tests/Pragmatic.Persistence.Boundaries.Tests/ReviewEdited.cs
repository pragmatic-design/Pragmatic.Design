using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

/// <summary>A review's new text: the event <see cref="EditReviewAction" /> declares, on the documents engine (#736).</summary>
public sealed record ReviewEdited(Guid Id, string Text, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
