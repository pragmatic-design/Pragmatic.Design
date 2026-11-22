namespace Pragmatic.Persistence.Documents;

/// <summary>A document a session writes (<see cref="DocCommitHooks" />), and how.</summary>
/// <param name="Entity">The entity, as the caller holds it.</param>
/// <param name="Kind">The write.</param>
public readonly record struct DocWrittenDocument(object Entity, DocWriteKind Kind);
