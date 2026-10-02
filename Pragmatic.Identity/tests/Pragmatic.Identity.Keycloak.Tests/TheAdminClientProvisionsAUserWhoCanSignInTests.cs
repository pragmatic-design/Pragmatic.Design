using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Keycloak.Tests;

/// <summary>
///     What <see cref="IKeycloakAdminClient" /> sends to the realm's admin REST API to provision a user who
///     can sign in: attributes, a password, the realm roles an application invents, and removing the
///     default roles Keycloak grants.
/// </summary>
/// <remarks>
///     <para>
///         Creating a user and assigning an existing role is not enough to provision one who can sign
///         in against a live Keycloak. The shapes asserted here are the ones Keycloak 26.2 accepts: the
///         live half is a consumer's Keycloak run, which provisions through this client alone.
///     </para>
///     <para>
///         The requests are recorded by the primary handler of every <c>HttpClient</c> the container
///         builds, so the client is reached the way an application reaches it — through
///         <c>UseKeycloakAuthentication</c> with a confidential client — and not constructed by hand.
///     </para>
/// </remarks>
public class TheAdminClientProvisionsAUserWhoCanSignInTests
{
    private const string Admin = "https://keycloak.example.com/admin/realms/shunpo";

    [Fact]
    public async Task CreateUser_SendsItsAttributes()
    {
        var (client, recorder) = Build();

        await client.CreateUserAsync(new KeycloakUser("ada", Email: "ada@example.com",
            Attributes: new Dictionary<string, IReadOnlyList<string>> { ["subject"] = ["u-ada"] }));

        var create = recorder.Single(HttpMethod.Post, $"{Admin}/users");
        create.Json.GetProperty("attributes").GetProperty("subject")[0].GetString().Should().Be("u-ada");
    }

    /// <summary>The control: a user with no attributes sends none, rather than an empty object Keycloak must merge.</summary>
    [Fact]
    public async Task CreateUser_WithoutAttributes_SendsNone()
    {
        var (client, recorder) = Build();

        await client.CreateUserAsync(new KeycloakUser("ada"));

        recorder.Single(HttpMethod.Post, $"{Admin}/users").Json.TryGetProperty("attributes", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task SetPassword_ResetsTheCredential()
    {
        var (client, recorder) = Build();

        await client.SetPasswordAsync("id-1", "s3cret", temporary: false);

        var reset = recorder.Single(HttpMethod.Put, $"{Admin}/users/id-1/reset-password");
        reset.Json.GetProperty("type").GetString().Should().Be("password");
        reset.Json.GetProperty("value").GetString().Should().Be("s3cret");
        reset.Json.GetProperty("temporary").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task EnsureRealmRole_WhenTheRealmHasNone_CreatesIt()
    {
        var (client, recorder) = Build(roleExists: false);

        await client.EnsureRealmRoleAsync("editor");

        recorder.Single(HttpMethod.Post, $"{Admin}/roles").Json.GetProperty("name").GetString().Should().Be("editor");
    }

    /// <summary>The control: a role the realm already has is not created again.</summary>
    [Fact]
    public async Task EnsureRealmRole_WhenTheRealmHasIt_CreatesNothing()
    {
        var (client, recorder) = Build(roleExists: true);

        await client.EnsureRealmRoleAsync("editor");

        recorder.Requests.Should().NotContain(r => r.Method == HttpMethod.Post && r.Uri == $"{Admin}/roles");
    }

    [Fact]
    public async Task RemoveRealmRole_DeletesTheUsersMapping()
    {
        var (client, recorder) = Build(roleExists: true);

        await client.RemoveRealmRoleAsync("id-1", "default-roles-shunpo");

        var delete = recorder.Single(HttpMethod.Delete, $"{Admin}/users/id-1/role-mappings/realm");
        delete.Json[0].GetProperty("name").GetString().Should().Be("default-roles-shunpo");
        delete.Json[0].GetProperty("id").GetString().Should().Be("role-id");
    }

    private static (IKeycloakAdminClient Client, Recorder Recorder) Build(bool roleExists = true)
    {
        var recorder = new Recorder(roleExists);
        var builder = new FakeBuilder();
        builder.Services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => recorder));
        builder.UseKeycloakAuthentication(k =>
        {
            k.BaseUrl = "https://keycloak.example.com";
            k.Realm = "shunpo";
            k.Audience = "shunpo-api";
            k.ClientId = "shunpo-admin";
            k.ClientSecret = "secret";
        });

        return (builder.Services.BuildServiceProvider().GetRequiredService<IKeycloakAdminClient>(), recorder);
    }

    private sealed record Recorded(HttpMethod Method, string Uri, string? Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body ?? "null").RootElement;
    }

    /// <summary>Answers the token endpoint and the admin API the way Keycloak does, and keeps every request.</summary>
    private sealed class Recorder(bool roleExists) : HttpMessageHandler
    {
        private readonly List<Recorded> _requests = [];

        public IReadOnlyList<Recorded> Requests => _requests;

        public Recorded Single(HttpMethod method, string uri)
            => _requests.Should().ContainSingle(r => r.Method == method && r.Uri == uri).Subject;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!.GetLeftPart(UriPartial.Path);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _requests.Add(new Recorded(request.Method, uri, body));

            if (uri.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
                return Json("""{"access_token":"admin-token"}""");

            if (request.Method == HttpMethod.Post && uri == $"{Admin}/users")
                return new HttpResponseMessage(HttpStatusCode.Created) { Headers = { Location = new Uri($"{Admin}/users/id-1") } };

            if (request.Method == HttpMethod.Get && uri.StartsWith($"{Admin}/roles/", StringComparison.Ordinal))
            {
                var name = Uri.UnescapeDataString(uri[($"{Admin}/roles/".Length)..]);
                return roleExists
                    ? Json($$"""{"id":"role-id","name":"{{name}}"}""")
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return request.Method == HttpMethod.Post && uri == $"{Admin}/roles"
                ? new HttpResponseMessage(HttpStatusCode.Created)
                : new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
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
