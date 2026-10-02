namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     An account as a person knows it: a name, a work email and a password.
/// </summary>
public sealed record TestAccount(string FullName, string WorkEmail, string Password);
