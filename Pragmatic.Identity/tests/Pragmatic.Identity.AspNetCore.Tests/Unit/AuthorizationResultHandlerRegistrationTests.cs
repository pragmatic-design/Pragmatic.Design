using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Authorization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

/// <summary>
///     The handler that explains a 403 has to be the one the middleware resolves.
/// </summary>
/// <remarks>
///     A lab consumer reported an empty 403 body on a build that already contained
///     <see cref="PragmaticAuthorizationResultHandler" />, which is the shape of a registration that
///     never wins rather than a handler that never works. Asserting the resolved type is the only
///     check that tells the two apart — the class existing proves nothing.
/// </remarks>
public sealed class AuthorizationResultHandlerRegistrationTests
{
    [Fact]
    public void AddPragmaticAuthorization_AfterAddAuthorization_StillWins()
    {
        var services = new ServiceCollection();

        // The order every entry point uses: ASP.NET first, ours second.
        services.AddAuthorization();
        services.AddPragmaticAuthorization();

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        resolved.Should().BeOfType<PragmaticAuthorizationResultHandler>();
    }

    [Fact]
    public void AddPragmaticAuthorization_WhenApplicationRegistersItsOwn_KeepsTheApplicationOne()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, CustomHandler>();

        services.AddAuthorization();
        services.AddPragmaticAuthorization();

        var resolved = services.BuildServiceProvider()
            .GetRequiredService<IAuthorizationMiddlewareResultHandler>();

        resolved.Should().BeOfType<CustomHandler>(
            "an application that wrote its own handler must keep it");
    }

    private sealed class CustomHandler : IAuthorizationMiddlewareResultHandler
    {
        public Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
            => Task.CompletedTask;
    }
}
