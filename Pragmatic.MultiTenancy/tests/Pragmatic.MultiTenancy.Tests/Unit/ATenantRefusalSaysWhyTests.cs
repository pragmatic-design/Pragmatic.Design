using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Result;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Each refusal of the tenant middleware is a ProblemDetails with a stable code (#630), so a client can tell "your organisation is
///     suspended" from "not allowed", and "no such tenant" from "no such record".
/// </summary>
/// <remarks>
///     The refusals were a bare 403 or 404. The codes are what a client branches on; the title and detail go through the host's
///     <see cref="IErrorMessageResolver" />, like any other error's.
/// </remarks>
public sealed class ATenantRefusalSaysWhyTests
{
    private sealed class StubResolver(string? tenantId) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(tenantId);
    }

    /// <summary>Speaks Italian for the codes it knows, as an application's localizer would.</summary>
    private sealed class ItalianResolver : IErrorMessageResolver
    {
        public string? Resolve(string code, object? context = null)
            => code == "TENANT_SUSPENDED" ? "La tua organizzazione è sospesa." : null;

        public string? ResolveTitle(string code, object? context = null)
            => code == "TENANT_SUSPENDED" ? "Organizzazione sospesa" : null;
    }

    private static async Task<(int Status, string? ContentType, JsonElement Body)> Run(
        string? tenantId,
        TenantState? stored = null,
        string? claim = null,
        bool requireKnown = false,
        IErrorMessageResolver? messages = null)
    {
        var store = new InMemoryTenantStore();
        if (stored is { } state)
        {
            await store.CreateAsync(new TenantInfo
            {
                TenantId = tenantId!, TenantName = tenantId!, State = state, CreatedAt = DateTimeOffset.UnixEpoch,
            }).ConfigureAwait(false);
        }

        var services = new ServiceCollection().AddLogging();
        if (messages is not null)
            services.AddSingleton(messages);

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();
        if (claim is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", claim)], "Test"));

        var middleware = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            NullLogger<TenantResolutionMiddleware>.Instance,
            Options.Create(new MultiTenancyOptions { RequireTenant = true, RequireKnownTenant = requireKnown }));

        await middleware.InvokeAsync(context, new StubResolver(tenantId), new MutableTenantContext(), store).ConfigureAwait(false);

        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body).ConfigureAwait(false);
        return (context.Response.StatusCode, context.Response.ContentType, document.RootElement.Clone());
    }

    [Fact]
    public async Task ASuspendedTenant_Is403_TenantSuspended()
    {
        var (status, contentType, body) = await Run("acme", TenantState.Suspended);

        status.Should().Be(StatusCodes.Status403Forbidden);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("TENANT_SUSPENDED");
        body.GetProperty("tenantState").GetString().Should().Be("Suspended");
    }

    /// <summary>A tenant still provisioning is not suspended: the code says it is not active, and the state says why.</summary>
    [Fact]
    public async Task ATenantStillProvisioning_Is403_TenantNotActive()
    {
        var (status, _, body) = await Run("acme", TenantState.Provisioning);

        status.Should().Be(StatusCodes.Status403Forbidden);
        body.GetProperty("code").GetString().Should().Be("TENANT_NOT_ACTIVE");
        body.GetProperty("tenantState").GetString().Should().Be("Provisioning");
    }

    [Fact]
    public async Task AnUnknownTenant_Is404_TenantUnknown()
    {
        var (status, contentType, body) = await Run("nobody", requireKnown: true);

        status.Should().Be(StatusCodes.Status404NotFound);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("TENANT_UNKNOWN");
    }

    [Fact]
    public async Task ATenantOtherThanTheClaim_Is403_TenantMismatch()
    {
        var (status, contentType, body) = await Run("globex", claim: "acme");

        status.Should().Be(StatusCodes.Status403Forbidden);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("TENANT_MISMATCH");
    }

    [Fact]
    public async Task NoTenant_Is400_TenantRequired()
    {
        var (status, contentType, body) = await Run(tenantId: null);

        status.Should().Be(StatusCodes.Status400BadRequest);
        contentType.Should().StartWith("application/problem+json");
        body.GetProperty("code").GetString().Should().Be("TENANT_REQUIRED");
    }

    /// <summary>The words are the application's: a registered resolver writes the title and the detail, the code stays.</summary>
    [Fact]
    public async Task TheRefusal_SpeaksThroughTheErrorResolver()
    {
        var (_, _, body) = await Run("acme", TenantState.Suspended, messages: new ItalianResolver());

        body.GetProperty("title").GetString().Should().Be("Organizzazione sospesa");
        body.GetProperty("detail").GetString().Should().Be("La tua organizzazione è sospesa.");
        body.GetProperty("code").GetString().Should().Be("TENANT_SUSPENDED");
    }
}
