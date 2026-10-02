using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Actions.Invoker;
using Pragmatic.Configuration.Management.Actions;
using Pragmatic.Identity;
using Pragmatic.Result.Http;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Through the host, <c>SetConfigValue</c> writes a setting <c>ShowcaseOptions</c> declares and refuses
///     one nothing declares.
/// </summary>
/// <remarks>
///     The action reads the catalogue as an optional dependency, so this is what shows the generated
///     invoker hands it the host's: without it every write would be refused, and the unit tests — which
///     inject a catalogue by hand — would not notice.
/// </remarks>
public class OnlyADeclaredSettingIsWritableTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ADeclaredSetting_IsWritten()
    {
        var result = await SetAsync("Showcase:CancellationWindowHours", "48");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ASettingNothingDeclares_IsRefused()
    {
        var result = await SetAsync("Showcase:CancelationWindowHours", "48");

        result.Error.Should().BeOfType<BadRequestError>();
    }

    private async Task<Pragmatic.Result.VoidResult<Pragmatic.Result.IError>> SetAsync(string key, string value)
    {
        using var scope = Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(options.UserIdClaimType, $"operator-{Guid.NewGuid():N}"),
                new Claim(options.PermissionClaimType, "configuration.values.write"),
            ], "ConfigurationTest")),
        };

        return await scope.ServiceProvider
            .GetRequiredService<IVoidDomainActionInvoker<SetConfigValue>>()
            .InvokeAsync(new SetConfigValue { Key = key, Value = value });
    }
}
