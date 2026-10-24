namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A string column's length (<c>varchar(n)</c>, <c>nvarchar(n)</c>) in the schema the generator emits for Pragmatic.Migrations. Read by name,
///     as DataAnnotations' is: this one carries no validation, so it is trim and AOT clean (DataAnnotations' constructor is IL2026).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MaxLengthAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}
