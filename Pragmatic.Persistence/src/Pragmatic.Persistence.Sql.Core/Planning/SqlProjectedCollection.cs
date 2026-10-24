using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     A collection member of a declared projection: the entity's collection it reads, the child DTO (its own
///     <c>[MapFrom]</c> gives the columns and the reader), the child's foreign key column, and the generated
///     accessors the loader uses to group the children on the parent DTO's key and assign them.
/// </summary>
public sealed class SqlProjectedCollection
{
    public SqlProjectedCollection(string property, Type child, Type childDto, string foreignKeyColumn, Func<object, object?> parentKey, Action<object, List<object>> assign)
    {
        Property = property;
        Child = child;
        ChildDto = childDto;
        ForeignKeyColumn = foreignKeyColumn;
        ForeignKeyColumns = [foreignKeyColumn];
        ParentKey = parentKey;
        Assign = assign;
    }

    public string Property { get; }

    /// <summary>The child entity.</summary>
    public Type Child { get; }

    /// <summary>The child DTO, registered with <c>[MapFrom&lt;Child&gt;]</c>.</summary>
    public Type ChildDto { get; }

    public string ForeignKeyColumn { get; }

    /// <summary>A composite parent's: the child's columns holding its key, in its key order (<see cref="ParentKey" /> then gives a <see cref="SqlCompositeKey" />).</summary>
    public string[] ForeignKeyColumns { get; init; }

    /// <summary>The parent entity's key as the parent DTO holds it, boxed.</summary>
    public Func<object, object?> ParentKey { get; }

    /// <summary>Sets the member to the children (boxed rows of <see cref="ChildDto" />).</summary>
    public Action<object, List<object>> Assign { get; }
}
