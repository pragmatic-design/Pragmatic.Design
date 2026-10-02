namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     A private/protected field whose type <see cref="ServiceTypeDetector" /> could not classify as
///     either an injected dependency or plain state. Carried on the model so the feature can report the
///     diagnostic (PRAG0419 for actions/mutations, PRAG0527 for endpoints) at the trigger's location.
/// </summary>
internal sealed record AmbiguousDependencyInfo
{
    public required string FieldName { get; init; }

    /// <summary>The field's type in short display form — it is shown to the user.</summary>
    public required string TypeName { get; init; }
}
