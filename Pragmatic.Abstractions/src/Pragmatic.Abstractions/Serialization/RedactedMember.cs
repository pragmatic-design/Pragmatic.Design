namespace Pragmatic.Serialization;

/// <summary>Why a member must not leave the process in a log line or a serialized payload.</summary>
/// <remarks>
///     Two attributes, two intents, one exit. A secret has no data subject, no erasure right and no
///     row in an Article 30 register, so <c>[NotLogged]</c> and <c>[PersonalData]</c> stay separate
///     where they are declared. They share this channel because the question at the boundary is the
///     same one — may this value be written out — and answering it in two mechanisms is how one of
///     them ends up unwired.
/// </remarks>
public enum RedactionReason
{
    /// <summary>Marked <c>[NotLogged]</c>: a secret, an internal value, something with no shape a pattern could catch.</summary>
    NotLogged = 0,

    /// <summary>Marked <c>[PersonalData]</c>: personal data, which also carries a category.</summary>
    PersonalData = 1,
}

/// <summary>A member a type declared must not be logged, and the declaration it came from.</summary>
/// <param name="Name">
///     The SERIALIZED name — the <c>[JsonPropertyName]</c> value where a member is renamed, so it
///     matches the payload. Consumers compare it case-insensitively: without
///     <c>[JsonPropertyName]</c> the map carries the CLR name in PascalCase while the payload may be
///     camelCase.
/// </param>
/// <param name="Reason">Which declaration put it here.</param>
/// <param name="Category">
///     The personal-data category, when <paramref name="Reason" /> is
///     <see cref="RedactionReason.PersonalData" />; <see langword="null" /> otherwise. Carried so the
///     output can distinguish the two later without re-splitting the channel.
/// </param>
public readonly record struct RedactedMember(string Name, RedactionReason Reason, string? Category = null);
