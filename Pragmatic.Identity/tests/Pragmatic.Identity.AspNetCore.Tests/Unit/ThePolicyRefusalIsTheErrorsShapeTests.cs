using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Authorization;
using Pragmatic.Endpoints.Authorization;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.AspNetCore.Tests.Unit;

/// <summary>
///     The HTTP policy refuses a missing permission with the body the action pipeline writes
///     for the same refusal: <c>requiredPermissions</c>, <c>permissionMatch</c>, and the words of the
///     host's <see cref="IErrorMessageResolver" />. A handler that wrote a key of its own and its own
///     English sentence would give the same 403 two shapes, only one of them localized.
/// </summary>
public class ThePolicyRefusalIsTheErrorsShapeTests
{
    [Fact]
    public async Task AllOfSeveral_IsWrittenAsTheListAndTheMatch()
    {
        var body = await RefuseAsync(new PragmaticPermissionRequirement(["orders.read", "orders.write"])).ConfigureAwait(true);

        body.GetProperty("requiredPermissions").EnumerateArray().Select(p => p.GetString())
            .Should().Equal("orders.read", "orders.write");
        body.GetProperty("permissionMatch").GetString().Should().Be("all");
        body.GetProperty("code").GetString().Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task AnyOfSeveral_SaysAny()
    {
        var body = await RefuseAsync(new PragmaticPermissionRequirement(["orders.read", "orders.admin"], PermissionMode.Any)).ConfigureAwait(true);

        body.GetProperty("permissionMatch").GetString().Should().Be("any");
    }

    [Fact]
    public async Task TheRefusal_SpeaksTheHostsLanguage()
    {
        var body = await RefuseAsync(new PragmaticPermissionRequirement(["orders.read"]), new ItalianForbidden()).ConfigureAwait(true);

        body.GetProperty("title").GetString().Should().Be("Vietato");
        body.GetProperty("detail").GetString().Should().Be("Non hai il permesso richiesto.");
    }

    /// <summary>The control: without a resolver the refusal keeps the error's own words, naming the permission.</summary>
    [Fact]
    public async Task WithoutAResolver_TheErrorsOwnWords()
    {
        var body = await RefuseAsync(new PragmaticPermissionRequirement(["orders.read"])).ConfigureAwait(true);

        body.GetProperty("title").GetString().Should().Be("Forbidden");
        body.GetProperty("detail").GetString().Should().Be("The current user is missing the 'orders.read' permission.");
        body.GetProperty("instance").GetString().Should().Be("/orders");
    }

    private static async Task<JsonElement> RefuseAsync(IAuthorizationRequirement failed, IErrorMessageResolver? resolver = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (resolver is not null)
            services.AddSingleton(resolver);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Path = "/orders";
        context.Response.Body = new MemoryStream();

        var policy = new AuthorizationPolicyBuilder().AddRequirements(failed).Build();
        var refusal = PolicyAuthorizationResult.Forbid(AuthorizationFailure.Failed([failed]));

        await new PragmaticAuthorizationResultHandler().HandleAsync(_ => Task.CompletedTask, context, policy, refusal).ConfigureAwait(false);

        context.Response.StatusCode.Should().Be(403);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    private sealed class ItalianForbidden : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null) => code == "FORBIDDEN" ? "Non hai il permesso richiesto." : null;

        public string? ResolveTitle(string code, object? context = null) => code == "FORBIDDEN" ? "Vietato" : null;
    }
}
