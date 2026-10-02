namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     Generation mode for mapping expressions. Controls which code patterns are valid.
/// </summary>
internal enum GenerationMode
{
    /// <summary>
    ///     In-memory mapping (FromEntity). Supports converters, dictionaries, format strings,
    ///     method calls, if/else, loops. No expression tree restrictions.
    /// </summary>
    Runtime,

    /// <summary>
    ///     EF Core projection (Expression&lt;Func&lt;TSource, TTarget&gt;&gt;). Only object initializers,
    ///     inline lambdas, and SQL-translatable operations. No converters, no method bodies.
    /// </summary>
    ExpressionTree
}
