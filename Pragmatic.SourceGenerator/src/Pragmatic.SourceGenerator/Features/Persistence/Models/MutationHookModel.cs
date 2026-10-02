namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model representing a lifecycle hook found as a nested class inside a mutation DTO.
///     <para>
///         Hooks are nested classes with a static <c>Execute()</c> method that get called
///         at specific points in the mutation lifecycle.
///     </para>
/// </summary>
/// <example>
///     <code>
///     [Mutation&lt;Order&gt;]
///     public class UpdateOrderDto
///     {
///         public class AfterLoad
///         {
///             public static void Execute(UpdateOrderDto dto, Order entity, DbContext db) { ... }
///         }
///     }
///     </code>
/// </example>
internal sealed record MutationHookModel
{
    /// <summary>
    ///     The hook lifecycle point (e.g., AfterLoad, PreValues).
    /// </summary>
    public required MutationHookType HookType { get; init; }

    /// <summary>
    ///     The name of the nested class (e.g., "AfterLoad").
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    ///     Whether the Execute method is async (returns Task or Task&lt;T&gt;).
    /// </summary>
    public bool IsAsync { get; init; }
}

/// <summary>
///     Lifecycle hook points in the mutation pipeline.
/// </summary>
internal enum MutationHookType
{
    /// <summary>
    ///     Runs before the entity is loaded from the database.
    ///     Signature: Execute(TDto dto, DbContext db[, CancellationToken ct])
    /// </summary>
    PreLoad,

    /// <summary>
    ///     Runs after the entity is loaded/resolved, before property assignment.
    ///     Signature: Execute(TDto dto, TEntity entity, DbContext db[, CancellationToken ct])
    /// </summary>
    AfterLoad,

    /// <summary>
    ///     Runs before values are applied to the entity.
    ///     Signature: Execute(TDto dto, TEntity entity, DbContext db[, CancellationToken ct])
    /// </summary>
    PreValues,

    /// <summary>
    ///     Runs after all values (scalars, nested, collections) are applied.
    ///     Signature: Execute(TDto dto, TEntity entity, DbContext db[, CancellationToken ct])
    /// </summary>
    PostValues
}
