using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Identity.Local.Actions;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     Whether <c>UseJwtAuthentication</c> accepts a token is decided by its security stamp as much as by
///     its signature — and the getting-started page taught a token without one.
/// </summary>
/// <remarks>
///     <para>
///         <c>RequireSecurityStamp</c> is <c>true</c> by default, so a token with no <c>sstamp</c> claim is
///         refused; a token with one is checked against the <c>ILocalIdentityStore</c>. The page's example
///         called <c>Generate(subject, displayName, tenantId, roles, permissions)</c> and stopped there: a
///         401 on every request, and nothing on the page to explain it.
///     </para>
///     <para>
///         <see cref="IssueTokenAfterLogin" /> is the example as the page now shows it, compiled here so
///         the snippet cannot drift from the signatures it calls.
///     </para>
/// </remarks>
public class TheSecurityStampDecidesTheTokenTests
{
    private const string Key = "security-stamp-signing-key-at-least-32-chars!";
    private const string Subject = "local|u-1";
    private const string Pseudonym = "6f1c0e9a4b7d42e8a1c3f5b7d9e0a2c4";

    /// <summary>A token without a security stamp, under the default options.</summary>
    [Fact]
    public async Task AStamplessToken_IsRefused_ByDefault()
    {
        var provider = Host(requireStamp: true, store: null);
        var token = provider.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: Subject, displayName: "Grace Hopper", roles: ["owner"]).Token;

        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("no security stamp");
    }

    /// <summary>The two cases the page names for switching it off: the same token is accepted.</summary>
    [Fact]
    public async Task AStamplessToken_IsAccepted_WhenTheStampIsNotRequired()
    {
        var provider = Host(requireStamp: false, store: null);
        var token = provider.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: Subject, displayName: "Grace Hopper", roles: ["owner"]).Token;

        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeTrue(result.Failure?.Message ?? "");
    }

    /// <summary>The example as the page shows it now: the stamp the store holds, on the token.</summary>
    [Fact]
    public async Task ATokenCarryingTheStampTheStoreHolds_IsAccepted()
    {
        var store = new OneIdentity(new LocalIdentity { ExternalIdentityKey = Subject, SecurityStamp = "stamp-1" });
        var provider = Host(requireStamp: true, store);

        var token = (await IssueTokenAfterLogin(
            provider.GetRequiredService<JwtTokenGenerator>(), store,
            new LoginResult(Subject, TimeProvider.System.GetUtcNow()))).Token;

        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeTrue(result.Failure?.Message ?? "");
    }

    /// <summary>
    ///     The control, and what the stamp is for: after a password change rotates it, the token issued
    ///     before is refused.
    /// </summary>
    [Fact]
    public async Task ATokenFromBeforeTheStampRotated_IsRefused()
    {
        var identity = new LocalIdentity { ExternalIdentityKey = Subject, SecurityStamp = "stamp-1" };
        var store = new OneIdentity(identity);
        var provider = Host(requireStamp: true, store);
        var token = (await IssueTokenAfterLogin(
            provider.GetRequiredService<JwtTokenGenerator>(), store,
            new LoginResult(Subject, TimeProvider.System.GetUtcNow()))).Token;

        identity.SecurityStamp = "stamp-2";
        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("no longer valid");
    }

    /// <summary>
    ///     A token whose subject is not the identity key — a pseudonym, so that what records the
    ///     subject records no email — still finds its account through the key it carries.
    /// </summary>
    /// <remarks>
    ///     <c>Generate(subject, …, externalIdentityKey)</c> exists for exactly this, and
    ///     <c>IAuthenticationContext.ExternalIdentityKey</c> reads the key from that claim. The stamp check
    ///     looked the account up by <c>sub</c> instead, found none, and refused every such token.
    /// </remarks>
    [Fact]
    public async Task ATokenWhoseSubjectIsAPseudonym_FindsItsAccountThroughTheKeyItCarries()
    {
        var store = new OneIdentity(new LocalIdentity { ExternalIdentityKey = Subject, SecurityStamp = "stamp-1" });
        var provider = Host(requireStamp: true, store);

        var token = provider.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: Pseudonym, securityStamp: "stamp-1", externalIdentityKey: Subject).Token;

        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeTrue(result.Failure?.Message ?? "");
    }

    /// <summary>The control: the same token is still revoked by a rotation — the check ran, it did not pass by default.</summary>
    [Fact]
    public async Task ATokenWhoseSubjectIsAPseudonym_IsRefusedOnceTheStampRotated()
    {
        var identity = new LocalIdentity { ExternalIdentityKey = Subject, SecurityStamp = "stamp-1" };
        var provider = Host(requireStamp: true, new OneIdentity(identity));
        var token = provider.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: Pseudonym, securityStamp: "stamp-1", externalIdentityKey: Subject).Token;

        identity.SecurityStamp = "stamp-2";
        var result = await AuthenticateAsync(provider, token);

        result.Succeeded.Should().BeFalse();
        result.Failure!.Message.Should().Contain("no longer valid");
    }

    /// <summary>The getting-started example, step 5: the token for a user who just signed in.</summary>
    private static async Task<AccessToken> IssueTokenAfterLogin(
        JwtTokenGenerator jwtGenerator, ILocalIdentityStore identities, LoginResult loginResult)
    {
        // The stamp the store holds now: rotating it on a password change or reset is what revokes
        // every token issued before, so the token has to carry it.
        var identity = await identities.FindByExternalKeyAsync(loginResult.ExternalIdentityKey).ConfigureAwait(false)
                       ?? throw new InvalidOperationException("The identity that just signed in is not in the store.");

        return jwtGenerator.Generate(
            loginResult,
            displayName: "Grace Hopper",
            roles: ["owner"],
            securityStamp: identity.SecurityStamp);
    }

    private static ServiceProvider Host(bool requireStamp, ILocalIdentityStore? store)
    {
        var builder = new FakeBuilder();
        builder.Services.AddLogging();
        builder.Services.AddPragmaticIdentity();
        builder.UseJwtAuthentication(jwt =>
        {
            jwt.SigningKey = Key;
            jwt.Issuer = "https://app.example.com";
            jwt.Audience = "app-api";
            jwt.RequireSecurityStamp = requireStamp;
        });
        if (store is not null)
            builder.Services.AddSingleton(store);

        return builder.Services.BuildServiceProvider();
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(IServiceProvider provider, string token)
    {
        using var scope = provider.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        return await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
    }

    private sealed class OneIdentity(LocalIdentity identity) : ILocalIdentityStore
    {
        public ValueTask<LocalIdentity?> FindByEmailAsync(string email, CancellationToken ct = default)
            => ValueTask.FromResult<LocalIdentity?>(null);

        public ValueTask<LocalIdentity?> FindByExternalKeyAsync(string externalKey, CancellationToken ct = default)
            => ValueTask.FromResult(externalKey == identity.ExternalIdentityKey ? identity : null);

        public ValueTask<LocalIdentity> CreateAsync(LocalIdentity created, CancellationToken ct = default)
            => ValueTask.FromResult(created);

        public ValueTask UpdateAsync(LocalIdentity updated, CancellationToken ct = default) => ValueTask.CompletedTask;

        public ValueTask<bool> EmailExistsAsync(string email, CancellationToken ct = default) => ValueTask.FromResult(false);
    }

    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
