using System.Net;
using Pragmatic.Testing.Assertions;
using TimeOff.IntegrationTests.Infrastructure;

namespace TimeOff.IntegrationTests;

/// <summary>
///     Sign-in attempts from one address are capped: the per-account lockout stops guessing one
///     password, this stops trying one password against many accounts.
/// </summary>
/// <remarks>
///     The suite raises the limit for every other class (see <see cref="TimeOffWebFactory" />); this one
///     sets it to three. Under TestServer every request comes from the same address, which is exactly the
///     shape of a spray from one client.
/// </remarks>
public sealed class SigningInFromOneAddress(PostgresFixture database) : TimeOffTestBase(database)
{
    private const int Limit = 3;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Identity:Local:RateLimit:PermitLimit"] = Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Identity:Local:RateLimit:Window"] = "00:10:00"
    };

    [Fact]
    public async Task MoreAttemptsThanTheLimit_Answer429_AndTheRestOfTheApplicationStillAnswers()
    {
        // Different accounts, none of them real: a spray, not a guess at one password.
        for (var attempt = 0; attempt < Limit; attempt++)
            (await PostSignInAsync($"nobody-{attempt}@time-off.test", "Not-The-Pa55word!")).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, "within the limit the answer is the credential check's");

        (await PostSignInAsync("nobody-else@time-off.test", "Not-The-Pa55word!")).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);

        (await Client.GetAsync("/openapi/v1.json")).StatusCode.Should().Be(HttpStatusCode.OK,
            "the control: the cap is on signing in, not on the application");
    }
}
