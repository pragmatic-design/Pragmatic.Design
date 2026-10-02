namespace Pragmatic.Http;

/// <summary>
///     Endpoint metadata naming the package operation an exposed endpoint runs.
/// </summary>
/// <param name="OperationType">The operation — the <c>T</c> of <c>[ExposeEndpoint&lt;T&gt;]</c>.</param>
/// <remarks>
///     <para>
///         Emitted by the generated host on every endpoint it maps from <c>[ExposeEndpoint&lt;T&gt;]</c>. The
///         route is the application's choice — Time off signs in at <c>identity/local/sign-in</c>, another
///         application at <c>login</c> — so a package that has to recognise its own operation on the wire
///         reads this, not the path. The login rate limit does: a path prefix would protect nothing once
///         the route is named differently.
///     </para>
///     <para>
///         ⚠️ Only on exposed endpoints. A module's own endpoints are the module's, and nothing outside it
///         has had to recognise them.
///     </para>
/// </remarks>
public sealed record ExposedOperationMetadata(Type OperationType);
