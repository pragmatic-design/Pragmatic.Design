namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A permission an action or mutation acquired from the auto-derivation switch, rather than from a
///     hand-written <c>[RequirePermission]</c>: the operation type it belongs to, the name, and which of
///     the two mechanisms produced it (<c>"auto-derived"</c> or <c>"explicit"</c>).
/// </summary>
/// <remarks>
///     Carried from Actions to the manifest because the manifest is built from endpoint models, which
///     read attributes off the type — and a derived name is by definition on no attribute. Without this
///     the manifest would list every permission an assembly enforces except the ones it invented.
/// </remarks>
internal readonly record struct DerivedPermissionEntry(string OperationTypeFqn, string Name, string Source);
