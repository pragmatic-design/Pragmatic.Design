namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     An employee HR registered and who accepted the invitation: their account, and their id.
/// </summary>
public sealed record HiredEmployee(TestAccount Account, Guid Id);
