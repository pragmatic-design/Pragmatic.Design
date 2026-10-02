namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Marks a DomainAction or a Mutation that needs a referenced row to exist, without reading it. The invoker
///     checks it with the repository's <c>ExistsAsync</c> before the body — after authorization — and answers 404
///     when the key names nothing.
/// </summary>
/// <remarks>
///     <para>
///         The reference an operation carries but does not use: a foreign key in the body of a create. Loading it
///         with <c>[LoadEntity]</c> materializes a row nobody reads; leaving it to the database answers with a
///         foreign-key violation instead of a 404 naming the row. The check is an <c>EXISTS</c> through the same
///         repository and filters as a read.
///     </para>
///     <para>
///         The key property is of the entity's key type (<c>PRAG0411</c> otherwise). Declared nullable —
///         <c>Guid? TeamId</c> — a null key is not checked. On a mutation a key the entity has a member of is
///         still written to it, as any other input.
///     </para>
///     <para>
///         Beside a <c>[LoadEntity]</c> of the same entity and key it is <c>PRAG0462</c>, and only the load runs:
///         a row that was read exists.
///     </para>
/// </remarks>
/// <typeparam name="TEntity">The entity the key refers to.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireExistsAttribute<TEntity> : Attribute where TEntity : class
{
    /// <summary>Creates a RequireExists attribute.</summary>
    /// <param name="keyPropertyName">The name of the property on the action or mutation that holds the key.</param>
    public RequireExistsAttribute(string keyPropertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyPropertyName);
        KeyPropertyName = keyPropertyName;
    }

    /// <summary>The name of the property on the action or mutation that holds the key.</summary>
    public string KeyPropertyName { get; }
}
