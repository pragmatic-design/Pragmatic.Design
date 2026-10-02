namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a DomainAction as a composite that orchestrates multiple mutations
///     within a single transaction. The SG generates a transactional invoker
///     that calls each mutation's <c>ExecuteWithoutSave</c> and commits atomically.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CompositeActionAttribute : Attribute;
