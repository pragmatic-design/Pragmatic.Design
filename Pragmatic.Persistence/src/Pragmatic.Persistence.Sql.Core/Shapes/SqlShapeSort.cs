namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>A sort the client asked for: a whitelisted field and its direction.</summary>
public sealed record SqlShapeSort(string Field, bool Descending);
