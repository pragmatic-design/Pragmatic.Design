using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pragmatic.Composition;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     The JWT generator is the <see cref="IAccessTokenIssuer" /> the host registers, and the
///     token it signs for a sign-in says everything the sign-in established: who acts, the account's key,
///     and the security stamp that revokes it.
/// </summary>
public class TheIssuerSignsASignInTests
{
    private const string Key = "the-issuer-signs-a-sign-in-key-at-least-32!";

    [Fact]
    public void UseJwtAuthentication_RegistersTheIssuer()
    {
        var builder = new FakeBuilder();
        builder.UseJwtAuthentication(jwt => jwt.SigningKey = Key);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IAccessTokenIssuer>().Should().BeSameAs(
            provider.GetRequiredService<JwtTokenGenerator>(), "one generator, reached through its contract");
    }

    [Fact]
    public void TheToken_CarriesTheSubjectTheKeyTheStampAndWhatTheApplicationAdded()
    {
        var claims = new SignInClaims(new LocalIdentity
        {
            ExternalIdentityKey = "local|ada@example.com",
            SecurityStamp = "stamp-1"
        })
        {
            Subject = "ref-42",
            DisplayName = "Ada"
        };
        claims.Roles.Add("employee");

        var issuer = new JwtTokenGenerator(Options.Create(new JwtOptions { SigningKey = Key }));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issuer.Issue(claims).Token);

        token.Subject.Should().Be("ref-42");
        token.Claims.Should().Contain(c => c.Type == ExternalIdentityKey.ClaimType && c.Value == "local|ada@example.com");
        token.Claims.Should().Contain(c => c.Type == "sstamp" && c.Value == "stamp-1");
        token.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Ada");
        token.Claims.Should().Contain(c => c.Type == "role" && c.Value == "employee");
    }

    /// <summary>The control: the subject is the account's key when the application sets no other.</summary>
    [Fact]
    public void WithoutAContribution_TheSubjectIsTheAccountsKey()
    {
        var claims = new SignInClaims(new LocalIdentity { ExternalIdentityKey = "local|bob@example.com", SecurityStamp = "s" });

        var issuer = new JwtTokenGenerator(Options.Create(new JwtOptions { SigningKey = Key }));
        var token = new JwtSecurityTokenHandler().ReadJwtToken(issuer.Issue(claims).Token);

        token.Subject.Should().Be("local|bob@example.com");
    }

    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
