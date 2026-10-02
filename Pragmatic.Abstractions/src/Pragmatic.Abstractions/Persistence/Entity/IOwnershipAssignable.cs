namespace Pragmatic.Persistence.Entity;

/// <summary>
///     An owned entity whose owner the persistence layer may assign when it has none.
/// </summary>
/// <remarks>
///     <para>
///         Separate from <see cref="IOwnedEntity" />, which stays read-only, and deliberately so.
///         Widening that one to <c>{ get; set; }</c> — the shape <c>IAuditable</c> has — would hand every
///         caller the ability to <b>reassign ownership</b>, which is the one operation ownership exists
///         to prevent. Assigning is a capability of the layer that writes rows, not of anyone holding a
///         reference to an entity.
///     </para>
///     <para>
///         <b>Why it exists at all.</b> If <c>OwnerId</c> were set in one place only — the mutation
///         invoker — an entity created by an action through a repository would be written with no
///         owner, and the ownership filter would then hide it from everybody, including whoever had
///         just created it. Only a caller holding the bypass permission could see it, so a test suite
///         whose default permissions include that bypass would look healthy.
///     </para>
///     <para>
///         Auditing works the same way: <c>CreatedBy</c> is stamped by an interceptor at
///         <c>SaveChanges</c>, so it covers every write path rather than one. This interface is what lets
///         ownership do the same without a reflective setter — the generator implements it on entities
///         carrying <c>[HasOwner]</c>.
///     </para>
/// </remarks>
public interface IOwnershipAssignable : IOwnedEntity
{
    /// <summary>
    ///     Assigns the owner. Called only when the entity is being inserted with none.
    /// </summary>
    /// <param name="ownerId">The identifier of the user the row belongs to.</param>
    void AssignOwner(string ownerId);
}
