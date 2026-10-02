using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares that this property's value is generated from a format template.
/// </summary>
/// <remarks>
///     <para>
///         The source generator creates validation, parsing, and EF Core configuration
///         based on the format template.
///     </para>
///     <para>
///         Supported placeholders:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>{YYYY} - 4-digit year</description>
///         </item>
///         <item>
///             <description>{YY} - 2-digit year</description>
///         </item>
///         <item>
///             <description>{MM} - 2-digit month (01-12)</description>
///         </item>
///         <item>
///             <description>{DD} - 2-digit day (01-31)</description>
///         </item>
///         <item>
///             <description>{SEQ:N} - Sequence number with N digits (zero-padded)</description>
///         </item>
///         <item>
///             <description>{RANDOM:N} - Random alphanumeric with N characters</description>
///         </item>
///         <item>
///             <description>{GUID:N} - First N characters of a GUID</description>
///         </item>
///     </list>
///     <para>
///         Example usage:
///     </para>
///     <code>
/// public partial class Order
/// {
///     [GeneratedValue("ORD-{YYYY}{MM}-{SEQ:5}")]
///     public string OrderNumber { get; set; } // ORD-202601-00001
/// }
/// 
/// public partial class Invoice
/// {
///     [GeneratedValue("INV-{RANDOM:8}")]
///     public string InvoiceCode { get; set; } // INV-aB3kL9mN
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class GeneratedValueAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GeneratedValueAttribute" /> class.
    /// </summary>
    /// <param name="format">The format template the value is built from.</param>
    /// <exception cref="ArgumentNullException">Thrown when format is null.</exception>
    public GeneratedValueAttribute(string format)
    {
        ThrowIfNullOrWhiteSpace(format);
        Format = format;
    }

    /// <summary>
    ///     Gets the format template the value is built from.
    /// </summary>
    /// <value>
    ///     A format string with placeholders like {YYYY}, {SEQ:N}, {RANDOM:N}, etc.
    /// </value>
    public string Format { get; }

    /// <summary>
    ///     Gets or sets whether this key should be auto-generated on entity creation.
    /// </summary>
    /// <value>
    ///     True to auto-generate the key value; false for manual assignment only.
    ///     Default is true.
    /// </value>
    public bool AutoGenerate { get; set; } = true;

    /// <summary>
    ///     Gets or sets the name of the database sequence to use for {SEQ:N} placeholder.
    /// </summary>
    /// <value>
    ///     The sequence name. If not specified, a default name based on the property is used.
    /// </value>
    public string? SequenceName { get; set; }
}
