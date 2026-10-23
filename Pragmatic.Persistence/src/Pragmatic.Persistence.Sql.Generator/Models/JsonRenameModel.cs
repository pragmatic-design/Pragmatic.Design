namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A member of a JSON column whose JSON name is not its own: its path in the value, and the name it is written under.</summary>
internal sealed record JsonRenameModel(string Path, string Name);
