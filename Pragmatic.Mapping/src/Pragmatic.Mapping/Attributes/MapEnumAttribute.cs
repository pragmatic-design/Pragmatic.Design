namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     How a property whose target is an enum handles a value it does not recognise.
/// </summary>
/// <remarks>
///     <para>
///         Enums map <b>by name</b>, deliberately: two enums reordered do not produce wrong data in
///         silence. This attribute decides what happens when the name matches nothing — by default an
///         exception, which on a write path means a <c>500</c> for a request that was simply wrong.
///     </para>
///     <example>
///         <code>
/// [MapTo&lt;Order&gt;]
/// public partial class UpdateOrderDto
/// {
///     // The enum has a deliberate Unknown = 0; a value from an older client lands there.
///     [MapEnum(OnUnknown = UnknownEnumValue.Default)]
///     public string Channel { get; init; } = "";
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class MapEnumAttribute : Attribute
{
    /// <summary>
    ///     What to do with a string that names no member. Defaults to
    ///     <see cref="UnknownEnumValue.Throw" />.
    /// </summary>
    public UnknownEnumValue OnUnknown { get; set; } = UnknownEnumValue.Throw;

    /// <summary>Pairs by member name, the default.</summary>
    public MapEnumAttribute() => Matching = EnumMatch.ByName;

    /// <param name="matching">What pairs this enum with the one on the other side.</param>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <see cref="EnumMatch.ByValue" /> is a cast, so it also stops the compile-time check:
    ///         with <see cref="EnumMatch.ByName" /> a member the other side does not have is
    ///         <c>PRAG0328</c>, and by value there is nothing to check — any number converts.
    ///     </para>
    ///     <para>
    ///         A constructor argument rather than a named property, because <c>Match</c> is already a
    ///         member of <c>Attribute</c> and a property by that name hides it (CS0108). The
    ///         specification's own example wrote it this way.
    ///     </para>
    /// </remarks>
    public MapEnumAttribute(EnumMatch matching) => Matching = matching;

    /// <summary>What pairs this enum with the one on the other side.</summary>
    public EnumMatch Matching { get; }

    /// <summary>
    ///     The name this member has on the wire, when it is not the member's own.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Declared on the enum <b>member</b>, not on the property: the wire name belongs to the
    ///         value, and every shape that carries it should agree about it. Without this, a name that
    ///         is not a C# identifier — <c>in-progress</c>, <c>2xx</c> — would mean writing a converter
    ///         class for a rename.
    ///     </para>
    ///     <example>
    ///         <code>
    /// public enum Status
    /// {
    ///     [MapEnum(Alias = "in-progress")]
    ///     InProgress
    /// }
    /// </code>
    ///     </example>
    ///     <para>
    ///         ⚠️ Does not combine with <c>[Flags]</c>. A combined value is <c>"Read, Write"</c>, and
    ///         an alias table has no entry for it; the two are answered by different machinery on
    ///         purpose, and mixing them would put a second parser beside the runtime's own.
    ///     </para>
    /// </remarks>
    public string? Alias { get; set; }
}
