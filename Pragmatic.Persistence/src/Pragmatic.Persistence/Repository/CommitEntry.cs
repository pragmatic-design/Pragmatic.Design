namespace Pragmatic.Persistence.Repository;

/// <summary>An entity an <see cref="ICommitObserver" /> sees written, and how.</summary>
/// <param name="Entity">The entity, as the application holds it.</param>
/// <param name="State">Whether it is added, modified or deleted.</param>
public readonly record struct CommitEntry(object Entity, CommitEntryState State);
