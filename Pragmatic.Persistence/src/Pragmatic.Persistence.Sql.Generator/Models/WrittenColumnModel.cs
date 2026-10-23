namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A column a writer binds: <see cref="Bind" /> yields the entity <c>e</c>'s value as the column stores it; <see cref="Cast" /> is PostgreSQL's cast on writes.</summary>
internal sealed record WrittenColumnModel(string Column, string Property, string Kind, string Bind, string? Cast);
