namespace Pragmatic.Identity;

/// <summary>
///     User profile data extracted from the user entity.
///     Well-known properties have dedicated accessors; additional
///     properties are exposed via the <see cref="Properties"/> dictionary.
/// </summary>
public interface IUserProfile
{
    /// <summary>The user's preferred culture (e.g., "it-IT", "en-US").</summary>
    string? PreferredCulture { get; }

    /// <summary>The user's preferred time zone (IANA, e.g., "Europe/Rome").</summary>
    string? TimeZone { get; }

    /// <summary>Additional profile properties beyond the well-known ones.</summary>
    IReadOnlyDictionary<string, string?> Properties { get; }
}
