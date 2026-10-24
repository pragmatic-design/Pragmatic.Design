namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a static method in a static partial class that takes a <c>SqlQuery&lt;T&gt;</c> first and returns a
///     query chain ending in a terminal: the declared form of a query. The generator reads the chain once and
///     emits an extension on <c>SqlSession</c> with the same name and the remaining parameters, whose body
///     binds the parameters' values straight into the prepared command: no expression tree per call. The
///     definition itself is never executed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class QueryAttribute : Attribute;
