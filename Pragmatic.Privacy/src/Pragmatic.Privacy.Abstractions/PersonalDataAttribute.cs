namespace Pragmatic.Privacy;

/// <summary>
///     Marks a property as personal data, and says what should happen to it when its subject is erased.
/// </summary>
/// <remarks>
///     <para>
///         The declaration is the source of truth for three separate things: the erasure plan, the
///         export handed to the subject, and the processing register. Keeping them derived from one
///         annotation is what stops the register from drifting away from what the code actually does —
///         which is the usual state of a processing register.
///     </para>
///     <para>
///         The attribute carries <b>classification</b>, not policy. A retention period belongs to the
///         legal basis for processing, not to the type: the same order is kept for ten years under a
///         fiscal obligation and until withdrawal under consent. Any default here is only a default.
///     </para>
///     <para>
///         <b>On a parameter of a <c>[LoggerMessage]</c> method</b> it says the argument is personal
///         data in a log line, and the Pragmatic generator writes it as the mask at the call site. Only
///         the category means anything there: a log argument is not stored, so erasure, retention and
///         encryption have nothing to act on. On any other parameter the attribute is inert, and the
///         analyzer reports it.
///     </para>
/// </remarks>
/// <param name="category">What kind of personal data this is.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
public sealed class PersonalDataAttribute(DataCategory category) : Attribute
{
    /// <summary>What kind of personal data this is.</summary>
    public DataCategory Category { get; } = category;

    /// <summary>What happens to this property when the subject is erased.</summary>
    public ErasureStrategy Erasure { get; init; } = ErasureStrategy.Null;

    /// <summary>
    ///     Why the value is kept. Required with <see cref="ErasureStrategy.Retain" />, and reported both
    ///     in the processing register and in the outcome the subject is given.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    ///     Whether the value is stored encrypted under the subject's own key.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Required by <see cref="ErasureStrategy.DestroyKey" />: destroying a key that protects
    ///         nothing would report an erasure that did not happen.
    ///     </para>
    ///     <para>
    ///         <b>What makes it true is the property's type.</b> A <c>ProtectedValue</c> is stored
    ///         through <c>ProtectedValueConverter</c>, which the entity configuration applies for you —
    ///         the package <c>Pragmatic.Cryptography.EFCore</c> has to be referenced, and <b>PRAG0652</b>
    ///         says so when it is not. ⚠️ Without that converter a <c>ProtectedValue</c> cannot be
    ///         stored at all, and this flag would be a claim about something impossible.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>It changes how the value is read, and the change is not optional.</b> Reading
    ///         protected data is asynchronous and has three outcomes, one of which is "this subject was
    ///         erased" — so it goes through <c>ISubjectDataProtector.TryReadAsync</c> and the caller
    ///         handles the outcome. A property getter can express none of that, which means <b>a
    ///         protected field cannot be returned by a generated projection</b>: a DTO exposes what the
    ///         reader decrypted, not the column.
    ///     </para>
    /// </remarks>
    public bool Encrypted { get; init; }
}
