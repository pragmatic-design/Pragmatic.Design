namespace Pragmatic.Notifications;

/// <summary>A user's addresses, as an <see cref="IRecipientDirectory" /> knows them: either may be missing.</summary>
/// <param name="Email">The user's e-mail address; null when the directory has none.</param>
/// <param name="Phone">The user's phone number; null when the directory has none.</param>
public sealed record RecipientContact(string? Email, string? Phone);
