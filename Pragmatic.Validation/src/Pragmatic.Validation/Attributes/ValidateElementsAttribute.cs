namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Stops element validation at the first invalid element.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It does not switch element validation on.</b> The generator validates a collection's
///         elements whenever the element type implements <see cref="ISyncValidator" /> — with indexed
///         error paths, <c>Items[0].ProductId</c> — and it does that with or without this attribute.
///         "The generator walks the elements when this is applied to a collection property" reads as a
///         cause and is a coincidence: the attribute's own precondition and the generator's condition
///         are the same condition.
///     </para>
///     <para>
///         So what is left for it to say is <see cref="StopOnFirstError" />, which does reach the
///         generated loop. Writing the attribute <b>bare</b> on a type that carries other validation
///         rules is <c>PRAG0223</c>, an error: an author who writes it believes they turned something
///         on.
///     </para>
///     <para>
///         ⚠️ <b>One case where the bare form is legal, and the reason it survived the error.</b> A
///         type enters the validation pipeline because some property carries an attribute or a
///         <c>required</c> modifier. On a type where this is the only annotation — a record holding
///         nothing but a <c>List&lt;T&gt;</c> of validatables — deleting it produces no validator at
///         all, not a validator that skips the elements. There the attribute does decide something,
///         and <c>PRAG0223</c> stays silent.
///     </para>
///     <para>
///         The element type must implement <see cref="ISyncValidator" /> (typically by having
///         validation attributes and being a partial type) — where it does not, the attribute is
///         <c>PRAG0205</c>, because there is nothing to stop at.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record PlaceOrderRequest
/// {
///     [Required]
///     [NotEmpty]
///     // Each OrderItemRequest is validated because it is an ISyncValidator. This says: stop at the
///     // first bad one rather than collecting every error. Without the setting, the attribute is
///     // PRAG0223 — the walk happens anyway.
///     [ValidateElements(StopOnFirstError = true)]
///     public List&lt;OrderItemRequest&gt; Items { get; init; }
/// }
/// 
/// public partial record OrderItemRequest
/// {
///     [Required]
///     public ProductId ProductId { get; init; }
/// 
///     [Range(1, 100)]
///     public int Quantity { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidateElementsAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets whether to stop validating at the first invalid element.
    ///     Default is <c>false</c> (validate all elements).
    /// </summary>
    /// <remarks>
    ///     When <c>true</c>, validation stops at the first element that fails.
    ///     When <c>false</c> (default), all elements are validated and all errors collected.
    /// </remarks>
    public bool StopOnFirstError { get; set; }
}