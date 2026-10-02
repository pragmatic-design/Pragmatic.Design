namespace Pragmatic.Identity;

/// <summary>
///     Fills a property of a declared query, an action or a mutation from the caller: the generated
///     invoker writes it after validation and authorization, before the read or the body.
/// </summary>
/// <remarks>
///     <para>
///         Without a member it binds <see cref="ICurrentUser.Id" />, and the property is a
///         <see cref="string" />. With a member — <c>[FromCurrentUser(nameof(Employee.Id))]</c> — it binds
///         that member of the application's <see cref="PragmaticUserAttribute">[PragmaticUser]</see>
///         entity, read through the generated <c>{User}Resolver</c>, and the property has the member's type.
///     </para>
///     <para>
///         The property is written <c>{ get; private set; }</c>. It is not a parameter anywhere — not a
///         query-string or route parameter, not in the OpenAPI document, not settable by an in-process
///         caller — and it is still a property, so it is part of the cache key and of the serialized query.
///         A caller that is not authenticated gets <c>UnauthorizedError</c>; an authenticated caller with
///         no user entity gets <c>NotFoundError</c>.
///     </para>
///     <para>
///         <c>PRAG0730</c> reports a property the caller could set; <c>PRAG0731</c> a binding that cannot be
///         generated.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FromCurrentUserAttribute : Attribute
{
    /// <summary>Binds <paramref name="member" /> of the <c>[PragmaticUser]</c> entity, or the caller's id.</summary>
    /// <param name="member">
    ///     The member's name, written with <c>nameof</c>; <c>null</c> binds <see cref="ICurrentUser.Id" />.
    /// </param>
    public FromCurrentUserAttribute(string? member = null) => Member = member;

    /// <summary>The member of the user entity to bind, or <c>null</c> for <see cref="ICurrentUser.Id" />.</summary>
    public string? Member { get; }
}
