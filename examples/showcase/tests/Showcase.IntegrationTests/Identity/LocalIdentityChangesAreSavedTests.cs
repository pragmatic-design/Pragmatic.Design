using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Permissions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Identity;

/// <summary>
///     What a local identity operation changes is saved: the changed password is the one that signs in,
///     the reset token that was mailed confirms the reset, and failed sign-ins lock the account.
/// </summary>
/// <remarks>
///     <para>
///         Each operation finds the identity, changes it, and asks the store to save. A store that
///         found it with <c>AsNoTracking</c> would make the change on a detached copy, save nothing,
///         and still answer success — and a test that asserts status codes is satisfied by a store
///         that saves nothing.
///     </para>
///     <para>
///         So every test here signs in afterwards and reads the effect. The control is the sign-in
///         below the lockout threshold: it succeeds whether or not the failures are counted, so a
///         lockout test cannot pass by refusing everything.
///     </para>
///     <para>
///         A host of its own: the reset token reaches a mailbox this class registers, and the shared
///         host's notifier only writes to the log.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LocalIdentityChangesAreSavedTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string BaseUrl = "/identity/local";

    /// <summary><c>LocalIdentityOptions.MaxFailedLoginAttempts</c>: the Showcase keeps the default.</summary>
    private const int FailuresBeforeLockout = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Mailbox _mailbox = new();
    private ShowcaseWebFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ShowcaseWebFactory(fixture,
            extraServices: services => services.AddSingleton<IPasswordResetNotifier>(_mailbox));

        // The Showcase exposes the identity operations without [AllowAnonymous], so even sign-in is
        // called by an authenticated caller; this one only stands in for the front end.
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "identity-front-end");

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task ChangingThePassword_TheNewOneSignsIn_AndTheOldOneNoLonger()
    {
        var email = NewEmail("change");
        const string oldPassword = "Old@Pass123";
        const string newPassword = "New@Pass456";
        await RegisterAsync(email, oldPassword);
        var key = await SignInAsync(email, oldPassword);

        using var owner = _factory.CreateClient();
        owner.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        owner.DefaultRequestHeaders.Add("X-User-Id", key);
        owner.DefaultRequestHeaders.Add("X-User-External-Key", key);
        owner.DefaultRequestHeaders.Add("X-User-Permissions", LocalIdentityPermissions.ChangePassword);

        var change = await owner.PostAsJsonAsync($"{BaseUrl}/change-password",
            new { currentPassword = oldPassword, newPassword }, JsonOptions);
        change.IsSuccessStatusCode.Should().BeTrue(
            "the owner changes their own password: {0}", await change.Content.ReadAsStringAsync());

        (await LoginStatusAsync(email, newPassword)).Should().Be(HttpStatusCode.OK,
            "the new password is the one the account now has");
        (await LoginStatusAsync(email, oldPassword)).Should().Be(HttpStatusCode.Unauthorized,
            "the old password was replaced, not kept beside the new one");
    }

    [Fact]
    public async Task ConfirmingAResetWithTheMailedToken_SetsTheNewPassword()
    {
        var email = NewEmail("reset");
        const string newPassword = "Reset@Pass789";
        await RegisterAsync(email, "Before@Reset1");

        var request = await _client.PostAsJsonAsync($"{BaseUrl}/reset-password/request", new { email }, JsonOptions);
        request.IsSuccessStatusCode.Should().BeTrue();
        _mailbox.Tokens.TryGetValue(email, out var token).Should().BeTrue("the token is mailed, never returned");

        var confirm = await _client.PostAsJsonAsync($"{BaseUrl}/reset-password/confirm",
            new { email, token, newPassword }, JsonOptions);
        confirm.IsSuccessStatusCode.Should().BeTrue(
            "the token is the one that was issued: {0}", await confirm.Content.ReadAsStringAsync());

        (await LoginStatusAsync(email, newPassword)).Should().Be(HttpStatusCode.OK,
            "the reset set the new password");
    }

    [Fact]
    public async Task AfterTheConfiguredFailures_TheRightPasswordIsRefused()
    {
        var email = NewEmail("lock");
        const string password = "Right@Pass123";
        await RegisterAsync(email, password);

        for (var attempt = 0; attempt < FailuresBeforeLockout; attempt++)
            (await LoginStatusAsync(email, "Wrong@Pass000")).Should().Be(HttpStatusCode.Unauthorized);

        (await LoginStatusAsync(email, password)).Should().Be(HttpStatusCode.Unauthorized,
            "the account is locked: the right password is refused like any other, and the lockout is not revealed");
    }

    /// <summary>The control: below the threshold the right password still signs in.</summary>
    [Fact]
    public async Task BelowTheConfiguredFailures_TheRightPasswordStillSignsIn()
    {
        var email = NewEmail("nolock");
        const string password = "Right@Pass123";
        await RegisterAsync(email, password);

        for (var attempt = 0; attempt < FailuresBeforeLockout - 1; attempt++)
            (await LoginStatusAsync(email, "Wrong@Pass000")).Should().Be(HttpStatusCode.Unauthorized);

        (await LoginStatusAsync(email, password)).Should().Be(HttpStatusCode.OK,
            "one failure short of the lockout, the account is still open");
    }

    private static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@test.com";

    private async Task RegisterAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync($"{BaseUrl}/register", new { email, password }, JsonOptions);
        response.IsSuccessStatusCode.Should().BeTrue($"registering {email}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<string> SignInAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync($"{BaseUrl}/login", new { email, password }, JsonOptions);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return result.GetProperty("externalIdentityKey").GetString()!;
    }

    private async Task<HttpStatusCode> LoginStatusAsync(string email, string password)
        => (await _client.PostAsJsonAsync($"{BaseUrl}/login", new { email, password }, JsonOptions)).StatusCode;

    /// <summary>Where the reset tokens go instead of an inbox.</summary>
    private sealed class Mailbox : IPasswordResetNotifier
    {
        public ConcurrentDictionary<string, string> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task NotifyAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct = default)
        {
            Tokens[email] = token;
            return Task.CompletedTask;
        }
    }
}
