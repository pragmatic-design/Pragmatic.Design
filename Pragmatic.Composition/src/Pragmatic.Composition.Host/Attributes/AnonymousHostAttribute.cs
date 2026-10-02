namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Declares that this host deliberately has no authentication: its endpoints do not require
///     authorization by default. Applied to the host's <see cref="ModuleAttribute" /> class, next to its
///     <c>[Include&lt;…&gt;]</c> attributes.
/// </summary>
/// <remarks>
///     <para>
///         A household app on a LAN, a kiosk, an internal tool behind a reverse proxy that
///         authenticates. The source generator reads the declaration at compile time: the generated
///         endpoint root carries no <c>RequireAuthorization()</c>, and PRAG1695 — Authorization
///         referenced without Identity — is not reported.
///     </para>
///     <para>
///         Without it, a host that references <c>Pragmatic.Authorization</c> and not
///         <c>Pragmatic.Identity</c> fails the build with PRAG1695. Its endpoints would carry
///         authorization metadata with no authorization middleware in the pipeline, and ASP.NET Core
///         answers every such request with 500.
///     </para>
///     <para>
///         Only the root default is dropped. Authorization that an endpoint or a group declares for
///         itself is generated as before, and still needs something that authenticates.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AnonymousHostAttribute : Attribute
{
}
