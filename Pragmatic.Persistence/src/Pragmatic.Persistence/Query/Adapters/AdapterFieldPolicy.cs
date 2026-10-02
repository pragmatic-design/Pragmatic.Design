using System.Linq.Expressions;
using System.Reflection;

namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     Field policy for the client-driven grid adapters (PrimeNG/DevExpress). These adapters build
///     filter/sort expressions from a caller-supplied field name; without a guard a client could target
///     any property (e.g. <c>PasswordHash</c>) and use the result as a boolean oracle, or probe
///     navigation/complex properties. This policy restricts the exposed surface to non-sensitive scalar
///     properties.
/// </summary>
/// <remarks>
///     <para>
///         It is an <b>allowlist</b> where the entity published one: <see cref="GridFieldRegistry" />
///         carries the fields <c>[GenerateGridBridge]</c> computed, and a name outside them does not
///         resolve. The denylist and the scalar check stay behind it, as defence in depth and as the
///         whole guard for an entity that declared nothing.
///     </para>
///     <para>
///         ⚠️ The denylist alone is not an allowlist. Under it any public scalar not named below
///         resolves — a wider surface than the entity declares, on the one path where the
///         <b>client</b> supplies the field name. Filtering or sorting on a column makes it talk
///         without returning it, so a column nobody published is an oracle: <c>sortField</c> ranks by
///         it, <c>equals</c> answers whether a row holds a value.
///     </para>
/// </remarks>
internal static class AdapterFieldPolicy
{
    // The names live in Pragmatic.Contracts.SensitiveGridFieldNames, linked as source into this
    // assembly and into the source generator, so the runtime adapters and the generated
    // [GridAdapter]/[GenerateGridBridge] paths present the same guarded surface.
    // ⚠️ One source, not two hand-maintained copies: two copies drift apart, and on a list like this
    // the drift is a field one path guards and the other exposes.

    /// <summary>
    ///     The member access for <paramref name="field" /> on <paramref name="parameter" />, or
    ///     <c>null</c> when the field must not be exposed to a client-driven filter or sort — the caller
    ///     skips it.
    /// </summary>
    /// <param name="parameter">The lambda parameter the expression is built over.</param>
    /// <param name="field">The field name the client supplied.</param>
    /// <remarks>
    ///     <para>
    ///         Three guards, in the order that decides fastest: the entity's own declaration
    ///         (<see cref="GridFieldRegistry" />) when it published one, the denylist of names that must
    ///         never be exposed, and the scalar check that blocks navigation and complex-type probing.
    ///     </para>
    ///     <para>
    ///         ⚠️ The lookup is still a reflective one, and deliberately. <c>Expression.PropertyOrField</c>
    ///         would read as the tidier form and carries <c>RequiresUnreferencedCode</c>, so it trades a
    ///         counted <c>GetProperty</c> for a counted <c>IL2026</c> — the same reflection with a worse
    ///         trim story, which is the distinction between «no reflection» and «no IL warnings» this
    ///         repository has had to learn twice. The annotation is what makes the requirement travel:
    ///         <c>TEntity</c> declares what must be kept, and the declaration propagates to every caller
    ///         instead of leaking as a field that silently stops resolving in a trimmed build.
    ///     </para>
    ///     <para>
    ///         Both property sets, not just the public one: <c>BindingFlags.IgnoreCase</c> makes the
    ///         lookup walk every property to compare names, so the trimmer needs them all present even
    ///         though <c>BindingFlags.Public</c> means only a public one can ever be returned.
    ///     </para>
    /// </remarks>
    public static MemberExpression? ResolveQueryableMember<
        [global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
            global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties
            | global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.NonPublicProperties)]
        TEntity>(ParameterExpression parameter, string? field)
    {
        if (string.IsNullOrEmpty(field))
            return null;

        // The entity's own list wins where it exists. Null means «declared nothing», which is not the
        // same as «declared nothing allowed» — see GridFieldRegistry for why the two are distinct.
        if (GridFieldRegistry.IsDeclared(typeof(TEntity), field!) is false)
            return null;

        var property = typeof(TEntity).GetProperty(
            field, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null
            || global::Pragmatic.Contracts.SensitiveGridFieldNames.IsSensitive(property.Name)
            || !IsScalar(property.PropertyType))
        {
            return null;
        }

        return Expression.Property(parameter, property);
    }

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive
            || t.IsEnum
            || t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
            || t == typeof(Guid);
    }
}
