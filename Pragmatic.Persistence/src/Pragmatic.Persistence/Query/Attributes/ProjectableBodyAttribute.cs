namespace Pragmatic.Persistence.Query.Attributes;

/// <summary>
///     The body of a <see cref="ProjectableAttribute" /> member, as text, published by the source
///     generator on the member's generated <c>Expr</c> property. Not written by hand.
/// </summary>
/// <remarks>
///     <para>
///         A projection that reads a projectable member writes its body, so the database computes it:
///         the getter is no column, and EF Core would run it in memory over navigations nobody loaded.
///         When the member is compiled into another assembly, the projection's generator has no syntax
///         to read the body from — so the member's own module declares it here, over a placeholder
///         source the reader replaces with whatever reaches the member.
///     </para>
///     <para>
///         The body is already expanded through the projectable members it names, and its types are
///         fully qualified.
///     </para>
/// </remarks>
/// <param name="body">The member's body, over the generator's placeholder source.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProjectableBodyAttribute(string body) : Attribute
{
    /// <summary>The member's body, over the generator's placeholder source.</summary>
    public string Body { get; } = body;
}
