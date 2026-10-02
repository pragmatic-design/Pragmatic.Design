namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks an entity as a polymorphic attachment — it can be attached to multiple
///     different owner entity types via OwnerType/OwnerId columns.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PolymorphicAttachmentAttribute : Attribute;
