namespace Pragmatic.Persistence.Entity;

/// <summary>
///     An entity: something with an identity that outlives a single request.
/// </summary>
/// <remarks>
///     <para>
///         The key is a <see cref="System.Guid" />, always, and the interface has no type parameter
///         for it: the generator fixes <c>System.Guid</c> in <c>EntityTransform</c> and in
///         <c>TraitPropertyResolver</c>, so a key type parameter would read as a choice that is not
///         one — an entity declaring another key type would still get a <c>Guid PersistenceId</c>
///         generated into it, and its author a compiler error in a file they cannot open.
///     </para>
/// </remarks>
public interface IEntity
{
    /// <summary>
    ///     The entity's technical key, assigned by the generated <c>Create()</c> factory and never
    ///     by the database.
    /// </summary>
    System.Guid PersistenceId { get; }
}
