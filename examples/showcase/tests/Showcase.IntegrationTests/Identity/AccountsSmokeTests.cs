using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Identity;

/// <summary>
///     Smoke tests for the Accounts boundary and local identity integration.
///     Verifies that:
///       - LocalIdentityPackage endpoints are mapped (register, login)
///       - the generated AppUser.LocalIdentityStore is resolved from DI (via successful registration)
///       - AccountsBoundary actions are wired correctly
/// </summary>
public class AccountsSmokeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Package Endpoint: /identity/local/register is mapped
    // =========================================================================

    [Fact]
    public async Task RegisterEndpoint_IsMapped_ReturnsNon404()
    {
        // The register endpoint comes from LocalIdentityPackage via [UsePackage<LocalIdentityPackage>].
        // If the package is properly fused, the endpoint should be mapped.
        var response = await PostAsync("/identity/local/register", new
        {
            email = $"smoke-{Guid.NewGuid():N}@test.com",
            password = "Test@1234!"
        });

        // We expect either success (201/200) or a validation/business error (400/409/422).
        // The key assertion is that the route IS mapped (not 404).
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "LocalIdentityPackage register endpoint should be mapped via [UsePackage<LocalIdentityPackage>]");
    }

    // =========================================================================
    // Package Endpoint: /identity/local/login is mapped
    // =========================================================================

    [Fact]
    public async Task LoginEndpoint_IsMapped_ReturnsNon404()
    {
        var response = await PostAsync("/identity/local/login", new
        {
            email = "nonexistent@test.com",
            password = "Wrong@1234!"
        });

        // Not found means the route is not mapped; any other status means it's working
        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "LocalIdentityPackage login endpoint should be mapped via [UsePackage<LocalIdentityPackage>]");
    }

    // =========================================================================
    // User Registration: creates user via the generated AppUser.LocalIdentityStore
    // =========================================================================

    [Fact]
    public async Task RegisterUser_ValidCredentials_CreatesUser()
    {
        var email = $"accounts-{Guid.NewGuid():N}@test.com";

        var response = await PostAsync("/identity/local/register", new
        {
            email,
            password = "Secure@Pass1"
        });

        // Successful registration indicates the generated AppUser.LocalIdentityStore is resolved and functioning
        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "Registration should succeed, proving the generated AppUser.LocalIdentityStore is resolved from DI");
    }

    // =========================================================================
    // Duplicate Registration: EmailExists check via the generated AppUser.LocalIdentityStore
    // =========================================================================

    [Fact]
    public async Task RegisterUser_DuplicateEmail_DoesNotLeakExistence()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.com";

        // First registration should succeed
        var first = await PostAsync("/identity/local/register", new
        {
            email,
            password = "Secure@Pass1"
        });
        first.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "First registration should succeed");

        // Second registration with the same email. With the secure default (RevealAccountState = false),
        // registration is NOT an enumeration oracle: the duplicate returns the same success shape as the
        // first, without confirming the account already exists. (Set RevealAccountState = true to restore
        // the legacy 409 EmailAlreadyExists behaviour.)
        var second = await PostAsync("/identity/local/register", new
        {
            email,
            password = "AnotherPass1!"
        });

        second.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "Uniform anti-enumeration default returns the same success shape for a duplicate email");
    }

    // =========================================================================
    // Login after Registration: full identity lifecycle
    // =========================================================================

    [Fact]
    public async Task LoginUser_AfterRegistration_Succeeds()
    {
        var email = $"login-{Guid.NewGuid():N}@test.com";
        const string password = "Login@Pass1";

        // Register
        var registerResponse = await PostAsync("/identity/local/register", new { email, password });
        registerResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        // Login
        var loginResponse = await PostAsync("/identity/local/login", new { email, password });

        loginResponse.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created],
            "Login with valid credentials should succeed after registration");
    }
}
