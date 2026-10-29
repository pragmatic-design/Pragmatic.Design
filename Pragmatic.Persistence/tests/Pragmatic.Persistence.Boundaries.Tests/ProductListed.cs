using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

/// <summary>A product was put in the catalog.</summary>
public sealed record ProductListed(string Name, DateTimeOffset OccurredAt) : IDomainEvent;
