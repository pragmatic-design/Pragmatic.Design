namespace Pragmatic.SourceGenerator.Features.Redaction.Models;

/// <summary>
///     One member a type declared must not be logged, and which declaration put it there.
/// </summary>
/// <param name="ContainingTypeFqn">The fully-qualified type that declares the member.</param>
/// <param name="SerializedName">
///     The name the payload actually serializes to — the <c>[JsonPropertyName]</c> value where the
///     member is renamed, the CLR name otherwise.
/// </param>
/// <param name="Reason">
///     <c>NotLogged</c> or <c>PersonalData</c>, matching
///     <c>Pragmatic.Serialization.RedactionReason</c>.
/// </param>
/// <param name="Category">The personal-data category, when the reason is <c>PersonalData</c>.</param>
internal readonly record struct RedactedMemberModel(
    string ContainingTypeFqn,
    string SerializedName,
    string Reason,
    string? Category);
