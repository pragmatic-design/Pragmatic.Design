namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     The <c>[PersonalData]</c> classification of a single property.
/// </summary>
/// <remarks>
///     <para>
///         Flat, and every field a string, bool or enum-as-string — so the record's generated equality is
///         a genuine value comparison and the incremental pipeline caches correctly. Storing the
///         attribute symbol instead would pin the whole compilation and re-render on every keystroke.
///     </para>
///     <para>
///         The strategy and category travel as their member names rather than as the runtime enums,
///         because the generator compiles against netstandard2.0 and must not reference the runtime
///         package it is describing.
///     </para>
/// </remarks>
internal sealed record PersonalDataModel
{
    /// <summary>The <c>DataCategory</c> member name, e.g. <c>Contact</c>.</summary>
    public required string Category { get; init; }

    /// <summary>The <c>ErasureStrategy</c> member name, e.g. <c>Null</c>.</summary>
    public required string Erasure { get; init; }

    /// <summary>Why the value is retained. Only meaningful with <c>Retain</c>.</summary>
    public string? Reason { get; init; }

    /// <summary>Whether the value is stored encrypted under the subject's key.</summary>
    public bool Encrypted { get; init; }

    /// <summary>True when the strategy keeps the value in place.</summary>
    public bool IsRetained => Erasure == "Retain";

    /// <summary>True when erasure happens by destroying the subject's key.</summary>
    public bool IsKeyDestruction => Erasure == "DestroyKey";

    /// <summary>Special categories, which carry their own diagnostics.</summary>
    public bool IsSpecialCategory => Category == "Special";

    /// <summary>True when erasure means assigning null to the property.</summary>
    public bool NullsTheField => Erasure == "Null";

    /// <summary>
    ///     True when erasing the subject means writing this field.
    /// </summary>
    /// <remarks>
    ///     <c>Delete</c> and <c>DestroyKey</c> are decisions about the row and the subject, taken by the
    ///     orchestrator; <c>Retain</c> writes nothing by definition. Only the remaining three need a way
    ///     to assign the property.
    /// </remarks>
    public bool WritesTheField => Erasure is "Null" or "Anonymize" or "Pseudonymize";

    /// <summary>
    ///     <c>Retain</c> without a stated reason. Reported rather than silently accepted: retaining data
    ///     against an erasure request is legitimate, retaining it without saying why is indistinguishable
    ///     from having forgotten to erase it.
    /// </summary>
    public bool IsRetainedWithoutReason => IsRetained && string.IsNullOrWhiteSpace(Reason);

    /// <summary>
    ///     <c>DestroyKey</c> on a property that is not encrypted — a key destruction that would protect
    ///     nothing and report an erasure that did not happen.
    /// </summary>
    public bool IsKeyDestructionWithoutEncryption => IsKeyDestruction && !Encrypted;
}
