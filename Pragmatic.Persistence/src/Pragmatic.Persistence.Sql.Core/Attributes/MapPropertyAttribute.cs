namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a DTO property: the entity path it reads (<c>"Customer.Name"</c>), when the name does not say it.
///     Same shape as the repository's Pragmatic.Mapping attribute, read by <c>Project&lt;TDto&gt;()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MapPropertyAttribute(string sourcePath) : Attribute
{
    public string SourcePath { get; } = sourcePath;
}
