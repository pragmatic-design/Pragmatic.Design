namespace Pragmatic.Notifications.Email;

/// <summary>The address a notification e-mail leaves from, and the name shown beside it.</summary>
/// <param name="Address">The <c>From</c> address.</param>
/// <param name="Name">The display name, or <see langword="null" /> for none.</param>
public sealed record EmailSenderIdentity(string Address, string? Name = null);
