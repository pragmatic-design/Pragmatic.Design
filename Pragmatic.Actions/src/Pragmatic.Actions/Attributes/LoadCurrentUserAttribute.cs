namespace Pragmatic.Actions.Attributes;

/// <summary>
///     Loads the signed-in user's entity — the application's <c>[PragmaticUser]</c> — before the operation
///     runs. The generator adds a field for it (<c>_current{User}</c>) and the invoker fills it after
///     authorization, through the generated <c>{User}Resolver</c>.
/// </summary>
/// <remarks>
///     <para>
///         A caller who is not authenticated is answered 401; an authenticated account with no user entity
///         is answered 404 — what <c>[FromCurrentUser]</c> answers on a query. The field is never null in
///         <c>Execute</c> or <c>ApplyAsync</c>.
///     </para>
///     <para>
///         It replaces the resolver field and the three lines every "as the signed-in user" operation wrote
///         by hand. <c>PRAG0451</c> reports an operation whose module has no user entity to load.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LoadCurrentUserAttribute : Attribute
{
    /// <summary>
    ///     Overrides the field's name. By default it is <c>_current</c> followed by the user entity's name
    ///     (<c>_currentEmployee</c>).
    /// </summary>
    public string? FieldName { get; set; }
}
