namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Static interface for entity types with a SG-generated <c>Create()</c> factory.
///     Enables zero-overhead, type-safe entity creation in generic contexts.
/// </summary>
/// <typeparam name="TSelf">The entity type (self-referencing).</typeparam>
/// <remarks>
///     <para>
///         This interface is automatically implemented by the SG when an entity is decorated
///         with <c>[Entity]</c> or <c>[Entity]</c>. The generated <c>Create()</c>
///         method initializes persistence ID (Guid v7), audit timestamps, and default values.
///     </para>
///     <para>
///         <c>ICreatable&lt;T&gt;</c> dispatches at compile time via <c>static abstract</c> — no virtual
///         call, no DI resolution — and is the only creation mechanism. There is no DI-resolved
///         entity factory.
///     </para>
///     <example>
///         <code>
///         // Generic factory using static dispatch:
///         public T CreateEntity&lt;T&gt;() where T : ICreatable&lt;T&gt; => T.Create();
///         </code>
///     </example>
/// </remarks>
public interface ICreatable<out TSelf> where TSelf : ICreatable<TSelf>
{
    /// <summary>
    ///     Creates a new instance with SG-generated initialization
    ///     (ID, audit fields, default values).
    /// </summary>
    static abstract TSelf Create();
}
