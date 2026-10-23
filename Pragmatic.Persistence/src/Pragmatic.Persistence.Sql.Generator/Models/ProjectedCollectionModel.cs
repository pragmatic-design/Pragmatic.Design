using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A DTO's collection member: the child entity and DTO, the child's foreign key column, and its foreign key columns under a composite parent.</summary>
internal sealed record ProjectedCollectionModel(string Property, string ChildEntityType, string ChildDtoType, string ForeignKeyColumn, EquatableArray<string> ForeignKeyColumns);
