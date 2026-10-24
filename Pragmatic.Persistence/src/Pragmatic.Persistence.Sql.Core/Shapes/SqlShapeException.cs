namespace Pragmatic.Persistence.Sql.Shapes;

/// <summary>A shape the client asked for that the plan cannot give: a field outside the whitelist, an operator it does not know, a value of the wrong kind.</summary>
public sealed class SqlShapeException(string message) : Exception(message);
