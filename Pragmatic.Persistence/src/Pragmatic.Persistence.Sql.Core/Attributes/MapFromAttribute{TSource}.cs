namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a DTO: the entity it projects. The generator registers the projection (columns and reader) at module
///     start, so a <c>Select</c> into this DTO built at runtime (a grid filter, a dynamic query) can be read
///     without compiling a tree. <c>Project&lt;TDto&gt;()</c> at compile time needs no attribute. Same shape as
///     the repository's Pragmatic.Mapping attribute. On a struct (a record struct) the projection allocates no object per row.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MapFromAttribute<TSource> : Attribute
    where TSource : class;
