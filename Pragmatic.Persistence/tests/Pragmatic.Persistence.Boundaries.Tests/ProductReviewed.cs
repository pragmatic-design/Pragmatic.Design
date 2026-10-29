using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

/// <summary>A product got a review.</summary>
public sealed record ProductReviewed(string Text, int Stars, DateTimeOffset OccurredAt) : IDomainEvent;
