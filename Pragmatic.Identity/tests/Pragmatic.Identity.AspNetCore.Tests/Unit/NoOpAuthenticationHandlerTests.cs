using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Mocking;
using Pragmatic.Tests.Generated;
using Pragmatic.Testing.Assertions;
using Pragmatic.Identity;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

public sealed class NoOpAuthenticationHandlerTests
{
    private const string SchemeName = "Test";

    [Fact]
    public async Task HandleAuthenticate_OutsideDevelopment_Throws()
    {
        var handler = await CreateHandlerAsync("Production", authenticatedUser: false);

        var act = () => handler.AuthenticateAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Production*");
    }

    [Fact]
    public async Task HandleAuthenticate_InDevelopment_WithAuthenticatedUser_ReturnsSuccess()
    {
        var handler = await CreateHandlerAsync("Development", authenticatedUser: true);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Ticket!.AuthenticationScheme.Should().Be(SchemeName);
    }

    [Fact]
    public async Task HandleAuthenticate_InDevelopment_WithoutAuthenticatedUser_ReturnsNoResult()
    {
        var handler = await CreateHandlerAsync("Development", authenticatedUser: false);

        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.None.Should().BeTrue();
    }

    private static async Task<NoOpAuthenticationHandler> CreateHandlerAsync(
        string environmentName, bool authenticatedUser)
    {
        var optionsMonitor = new OptionsMonitorOfAuthenticationSchemeOptionsMock();
        optionsMonitor.Get.Returns(new AuthenticationSchemeOptions());

        var env = new HostEnvironmentMock();
        env.EnvironmentName.Returns(environmentName);

        var handler = new NoOpAuthenticationHandler(
            optionsMonitor, NullLoggerFactory.Instance, UrlEncoder.Default, env);

        var context = new DefaultHttpContext();
        if (authenticatedUser)
        {
            var identity = new ClaimsIdentity(new[] { new Claim("sub", "user-1") }, authenticationType: "Header");
            context.User = new ClaimsPrincipal(identity);
        }

        var scheme = new AuthenticationScheme(SchemeName, SchemeName, typeof(NoOpAuthenticationHandler));
        await handler.InitializeAsync(scheme, context).ConfigureAwait(false);
        return handler;
    }
}
