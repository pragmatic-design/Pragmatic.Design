using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A DTO's collection read from its companion: each row built with the child DTO's construction, grouped on the parent key read after the
///     DTO's columns (<see cref="KeyRead" />), handed to each parent by <see cref="ParentKey" />. The rows are kept when a collection is nested.
/// </summary>
internal sealed record ProjectedLoadModel(
    string Name,
    string Parents,
    string Property,
    EquatableArray<LoadStepModel> Children,
    string KeyType,
    string ChildDtoType,
    string Construct,
    string KeyRead,
    string ParentKey) : LoadStepModel(Name, Parents, Property, Children);
