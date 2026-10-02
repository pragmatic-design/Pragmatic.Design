using System.Collections.Concurrent;
using Pragmatic.Identity.Local.Services;

namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>
///     Where the invitations and password resets land in the tests — the mailbox a person would read
///     them in, and the one thing about the host the tests replace.
/// </summary>
public sealed class TestMailbox : IPasswordResetNotifier
{
    private readonly ConcurrentDictionary<string, string> _latestTokenByEmail = new(StringComparer.OrdinalIgnoreCase);

    public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        _latestTokenByEmail[email] = token;
        return Task.CompletedTask;
    }

    /// <summary>The last token sent to <paramref name="email" />, or a failure that says none was.</summary>
    public string LatestTokenFor(string email) =>
        _latestTokenByEmail.TryGetValue(email, out var token)
            ? token
            : throw new Xunit.Sdk.XunitException($"No invitation or reset was sent to {email}.");
}
