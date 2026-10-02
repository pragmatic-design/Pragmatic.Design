using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Authorization;

/// <summary>
///     Tests JWT token generation and the Register → Login → JWT lifecycle.
///     Covers:
///       - JwtTokenGenerator produces valid tokens with correct claims
///       - Register + Login full lifecycle via LocalIdentityPackage
///       - Token claim embedding (sub, name, role, permission, tenant_id)
/// </summary>
public class JwtAuthenticationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // E2E: Register → Login → get LoginResult → generate JWT
    // =========================================================================

    [Fact]
    public async Task Register_Login_GenerateJwt_FullFlow()
    {
        var email = $"e2e-{Guid.NewGuid():N}@test.com";
        const string password = "E2e@Pass1";

        // 1. Register
        var registerResponse = await PostAsync("/identity/local/register", new { email, password });
        registerResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "Registration should succeed");

        // 2. Login → LoginResult
        var loginResponse = await PostAsync("/identity/local/login", new { email, password });
        loginResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "Login should succeed after registration");

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var externalKey = loginBody.GetProperty("externalIdentityKey").GetString()!;
        externalKey.Should().StartWith("local|",
            "Local identity key format is 'local|email'");

        // 3. Generate JWT from LoginResult
        var jwtOptions = Microsoft.Extensions.Options.Options.Create(
            new Pragmatic.Identity.Local.Jwt.JwtOptions
            {
                SigningKey = "e2e-test-signing-key-at-least-32-chars!",
                Issuer = "https://showcase.test"
            });
        var generator = new Pragmatic.Identity.Local.Jwt.JwtTokenGenerator(jwtOptions);

        var loginResult = new Pragmatic.Identity.Local.Actions.LoginResult(
            externalKey,
            loginBody.GetProperty("authenticatedAt").GetDateTimeOffset());

        var jwt = generator.Generate(loginResult,
            displayName: "E2E User",
            tenantId: "test-tenant",
            roles: ["booking-manager"],
            permissions: ["catalog.property.read"]);

        // 4. Verify JWT structure
        jwt.Token.Should().NotBeNullOrEmpty();
        jwt.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt.Token);
        token.Subject.Should().Be(externalKey);
        token.Claims.Should().Contain(c => c.Type == "name" && c.Value == "E2E User");
        token.Claims.Should().Contain(c => c.Type == "role" && c.Value == "booking-manager");
        token.Claims.Should().Contain(c => c.Type == "permission" && c.Value == "catalog.property.read");
    }

    [Fact]
    public async Task Login_WithWrongPassword_Fails()
    {
        var email = $"wrong-{Guid.NewGuid():N}@test.com";
        const string password = "Correct@Pass1";

        // Register
        var registerResponse = await PostAsync("/identity/local/register", new { email, password });
        registerResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created]);

        // Login with wrong password
        var loginResponse = await PostAsync("/identity/local/login",
            new { email, password = "WrongPassword1!" });

        loginResponse.StatusCode.Should().NotBe(HttpStatusCode.OK,
            "Login with wrong password should not succeed");
    }

    // =========================================================================
    // JWT Token Generation — unit-level (no HTTP)
    // =========================================================================

    [Fact]
    public void JwtTokenGenerator_ProducesValidToken_WithFullClaims()
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new Pragmatic.Identity.Local.Jwt.JwtOptions
            {
                SigningKey = "this-is-a-test-signing-key-32chars!",
                Issuer = "https://test.example.com",
                Audience = "showcase-api",
                TokenExpiration = TimeSpan.FromHours(1)
            });
        var generator = new Pragmatic.Identity.Local.Jwt.JwtTokenGenerator(options);

        var result = generator.Generate(
            subject: "local|user@example.com",
            displayName: "Test User",
            tenantId: "test-tenant",
            roles: ["booking-manager", "catalog-viewer"],
            permissions: ["billing.invoice.read"]);

        result.Token.Should().NotBeNullOrEmpty();
        result.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        result.ExpiresAt.Should().BeBefore(DateTimeOffset.UtcNow.AddHours(2));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);

        jwt.Issuer.Should().Be("https://test.example.com");
        jwt.Subject.Should().Be("local|user@example.com");
        jwt.Claims.Should().Contain(c => c.Type == "name" && c.Value == "Test User");
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == "test-tenant");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "booking-manager");
        jwt.Claims.Should().Contain(c => c.Type == "role" && c.Value == "catalog-viewer");
        jwt.Claims.Should().Contain(c => c.Type == "permission" && c.Value == "billing.invoice.read");
    }

    [Fact]
    public void JwtTokenGenerator_MinimalClaims_ProducesValidToken()
    {
        var options = Microsoft.Extensions.Options.Options.Create(
            new Pragmatic.Identity.Local.Jwt.JwtOptions
            {
                SigningKey = "this-is-a-test-signing-key-32chars!",
                Issuer = "https://test.example.com"
            });
        var generator = new Pragmatic.Identity.Local.Jwt.JwtTokenGenerator(options);

        var result = generator.Generate(subject: "local|minimal@example.com");

        result.Token.Should().NotBeNullOrEmpty();
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        jwt.Subject.Should().Be("local|minimal@example.com");
        jwt.Claims.Should().NotContain(c => c.Type == "role");
        jwt.Claims.Should().NotContain(c => c.Type == "permission");
    }

    // =========================================================================
    // Identity endpoints are mapped
    // =========================================================================

    [Fact]
    public async Task IdentityEndpoints_AreMapped()
    {
        var registerResponse = await PostAsync("/identity/local/register", new
        {
            email = $"mapped-{Guid.NewGuid():N}@test.com",
            password = "Mapped@Pass1"
        });
        registerResponse.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Register endpoint should be mapped");

        var loginResponse = await PostAsync("/identity/local/login", new
        {
            email = "nonexistent@test.com",
            password = "Wrong@1234!"
        });
        loginResponse.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "Login endpoint should be mapped");
    }
}
