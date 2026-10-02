using Pragmatic.Mapping.Attributes;

namespace Conformance.Poco.Shapes;

/// <summary>
///     A delivery: the target of the enum cell.
/// </summary>
/// <remarks>
///     <para>
///         No attributes: it is a POCO, like the rest of <c>Shapes/</c>.
///     </para>
///     <para>
///         <b>Two parameterized constructors with the same score</b>, deliberately: it is the only
///         shape in which <c>[MapConstructor]</c> <em>decides</em> something. The automatic choice
///         (<c>ConstructorAnalyzer.SelectBestConstructor</c>) counts the parameters that match the
///         DTO's properties, on a tie prefers the one with fewer parameters, and on a tie of both falls
///         back to declaration order — that is, nothing a reader can see. Here both match on two
///         parameters each, so without the attribute the first would win, and the reference would
///         arrive with its spaces.
///     </para>
///     <para>
///         ⚠️ Measured by removal: without <c>[MapConstructor]</c> the case goes red on
///         <c>Reference</c>. With an attribute that picks what the automatic choice would pick anyway —
///         two constructors of different score — the case would stay green, and the cell would claim
///         something that does not happen.
///     </para>
///     <para>
///         ⚠️ The constructor path converts the DTO's types to the parameters' types: the string that
///         becomes the enum is converted while it is passed, not assigned afterwards. Passing the DTO's
///         types as they are would be <c>CS1503</c> — and the constructor path is the one that exists
///         <em>for</em> targets whose parameters are not the wire's types.
///     </para>
/// </remarks>
public sealed class Delivery
{
    public string Reference { get; set; } = "";

    public DeliveryChannel Channel { get; set; }

    public Delivery()
    {
    }

    /// <summary>
    ///     The constructor the automatic choice would take: declared first, same score.
    ///     It takes the values as they arrive.
    /// </summary>
    public Delivery(string reference, DeliveryChannel channel)
    {
        Reference = reference;
        Channel = channel;
    }

    /// <summary>
    ///     The declared one, which normalizes the reference — which is how the case tells which one ran.
    /// </summary>
    /// <remarks>
    ///     Same two parameters, in a different order: two distinct signatures with the same score, the
    ///     minimal form of a tie. In a real domain the two would be «rehydrate a row» and «accept what
    ///     comes from the wire»; here the entities exist to cover combinations, not to model a domain.
    /// </remarks>
    [MapConstructor]
    public Delivery(DeliveryChannel channel, string reference)
    {
        Channel = channel;
        Reference = reference.Trim().ToUpperInvariant();
    }
}
