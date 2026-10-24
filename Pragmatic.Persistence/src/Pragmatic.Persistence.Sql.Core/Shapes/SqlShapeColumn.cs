namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>One column a shaped plan exposes to the client: its name (the row type's member) and the kind its values convert to.</summary>
public sealed record SqlShapeColumn(string Name, SqlParamKind Kind);
