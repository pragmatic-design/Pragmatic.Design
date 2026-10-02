namespace Pragmatic.Authorization;

/// <summary>
///     Opt-in marker: when present on an assembly, every operation in it — action, mutation and
///     <c>[Query]</c> alike — that carries no <c>[RequirePermission]</c>/<c>[RequireAnyPermission]</c>
///     requires an auto-derived permission.
///     The name is kebab-case and dotted, the same shape the generated entity CRUD constants use:
///     <c>{boundary}.{resource}.{verb}</c> when the type name opens with a recognised verb
///     (<c>AddGuestCommentAction</c> in the Billing boundary → <c>billing.guest-comment.add</c>), and
///     <c>{boundary}.{operation}</c> otherwise (<c>IssueRefundAction</c> → <c>billing.issue-refund</c>).
///     <c>[ExplicitPermission]</c> overrides the derived name; <c>[AllowAnonymous]</c> opts out entirely.
/// </summary>
/// <remarks>
///     <para>
///         Equivalent to setting the build property
///         <c>&lt;PragmaticAutoDerivePermissions&gt;true&lt;/&gt;</c>. Both exist for the same reason as the
///         serialization switch: the attribute makes the behaviour testable and works in project-reference
///         scenarios where the package's <c>CompilerVisibleProperty</c> <c>.props</c> is not imported.
///     </para>
///     <para>
///         <b>This changes the posture of the whole assembly.</b> Without it an action with no permission
///         attribute requires nothing; with it the same action is denied to anyone whose roles do not grant
///         the derived name. Turn it on only once the derived names are granted somewhere — they are
///         emitted into the generated permission catalog so a role can reference them.
///     </para>
/// </remarks>
/// <remarks>
///     <b>Where this is consumed.</b> <c>ActionsFeature.AutoDerive</c> reads it for actions and
///     mutations, <c>EndpointsFeature.AutoDerive</c> for queries — the derived name lands on the
///     query's route and on its invoker, the two doors a query is reached through — and the two feed one
///     <c>ActionPermissionCatalogTemplate</c>: the catalogue mapping a derived permission constant
///     back to its value. That indirection is what keeps
///     <c>[RequirePermission(SomeGeneratedConst)]</c> from failing open, because a generator cannot
///     resolve a constant it is itself creating in the same compilation.
///     <para>
///         It can also be switched on for a whole assembly through the MSBuild property
///         <c>PragmaticAutoDerivePermissions</c>, which the same file reads as
///         <c>build_property.PragmaticAutoDerivePermissions</c>. That path needs the property listed
///         as a <c>CompilerVisibleProperty</c> in <c>Directory.Build.props</c>: without that line
///         MSBuild keeps the value to itself, the generator never sees the opt-in, and nothing
///         reports it.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PragmaticAutoDerivePermissionsAttribute : Attribute;
