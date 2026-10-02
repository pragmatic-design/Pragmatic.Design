namespace Pragmatic.Authorization;

/// <summary>
///     Publishes a static list of permission values so that a role in <em>another</em> assembly can be
///     catalogued with what it grants:
///     <c>[PermissionSet] public static readonly string[] Granted = ["kb.read", "kb.write"];</c>.
/// </summary>
/// <remarks>
///     <para>
///         A role's <c>DefaultPermissions</c> may read a list held elsewhere. Inside one compilation the
///         generator follows the reference to the list's initializer and catalogues the values; across an
///         assembly boundary there is no initializer to read — a referenced assembly is metadata, and a
///         <c>static readonly string[]</c> has no constant value. Without this attribute the catalogue
///         showed such a role as granting <b>nothing</b>, while the runtime, which reads the property,
///         granted every entry. The generator now emits the values of a marked list as
///         <see cref="PermissionSetValuesAttribute" /> on its own assembly, and the compilation that reads
///         the list takes them from there.
///     </para>
///     <para>
///         ⚠️ Marking it is the declaration: the generator does not publish every static list of strings it
///         can see. A list nobody marked, in an assembly the build cannot read, is <c>PRAG1015</c> on the
///         role that reads it — the catalogue says it cannot tell, instead of saying "nothing".
///     </para>
///     <para>
///         The list has to be readable at compile time: a collection expression, an array initializer, or
///         a reference to one — the same shapes a role's own <c>DefaultPermissions</c> accepts.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PermissionSetAttribute : Attribute;
