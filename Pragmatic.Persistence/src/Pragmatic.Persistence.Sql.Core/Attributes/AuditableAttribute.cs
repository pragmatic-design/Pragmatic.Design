namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     Who wrote a row and when (the repository's <c>[Auditable]</c>, read by name): the entity declares <c>CreatedAt</c>, <c>CreatedBy</c>,
///     <c>UpdatedAt</c>, <c>UpdatedBy</c> (any of them); every row write stamps them from the clock and <see cref="SqlContext.Current" />'s user, an
///     insert the Created ones (which no update changes after), every write the Updated ones.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditableAttribute : Attribute;
