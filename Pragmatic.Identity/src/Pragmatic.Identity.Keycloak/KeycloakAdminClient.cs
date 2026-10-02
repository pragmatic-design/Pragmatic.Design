using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     Admin REST implementation of <see cref="IKeycloakAdminClient"/>: acquires a client-credentials token
///     and calls the realm's admin API. This type owns the request shaping so the rest of the app stays
///     IdP-agnostic.
/// </summary>
/// <remarks>
///     The request shapes are asserted in <c>Pragmatic.Identity.Keycloak.Tests</c>; the live half is
///     a consumer application, whose Keycloak fixture provisions every test user through this client alone
///     against Keycloak 26 and reads the realm roles back from the token.
/// </remarks>
internal sealed class KeycloakAdminClient(HttpClient http, IOptions<KeycloakOptions> options) : IKeycloakAdminClient
{
    private readonly KeycloakOptions _options = options.Value;

    public async Task<string> CreateUserAsync(KeycloakUser user, CancellationToken ct = default)
    {
        using var request = await AdminRequestAsync(HttpMethod.Post, "/users", ct).ConfigureAwait(false);
        request.Content = JsonContent.Create(
            new KeycloakAdminJson.User(user.Username, user.Email, user.FirstName, user.LastName, user.Enabled, user.Attributes),
            KeycloakAdminJson.Default.UserBody);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // Keycloak returns the new user's id in the Location header.
        var location = response.Headers.Location?.ToString() ?? "";
        return location[(location.LastIndexOf('/') + 1)..];
    }

    public async Task<bool> UserExistsAsync(string username, CancellationToken ct = default)
    {
        using var request = await AdminRequestAsync(
            HttpMethod.Get, $"/users?username={Uri.EscapeDataString(username)}&exact=true", ct).ConfigureAwait(false);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var users = await response.Content.ReadFromJsonAsync<List<UserRepresentation>>(ct).ConfigureAwait(false);
        return users is { Count: > 0 };
    }

    public async Task AssignRealmRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        var role = await FindRealmRoleAsync(roleName, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Keycloak realm role '{roleName}' was not found.");

        await RoleMappingsAsync(HttpMethod.Post, userId, role, ct).ConfigureAwait(false);
    }

    public async Task SetPasswordAsync(string userId, string password, bool temporary = false, CancellationToken ct = default)
    {
        using var request = await AdminRequestAsync(
            HttpMethod.Put, $"/users/{Uri.EscapeDataString(userId)}/reset-password", ct).ConfigureAwait(false);
        request.Content = JsonContent.Create(
            new KeycloakAdminJson.Credential("password", password, temporary), KeycloakAdminJson.Default.CredentialBody);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task EnsureRealmRoleAsync(string roleName, CancellationToken ct = default)
    {
        if (await FindRealmRoleAsync(roleName, ct).ConfigureAwait(false) is not null)
            return;

        using var request = await AdminRequestAsync(HttpMethod.Post, "/roles", ct).ConfigureAwait(false);
        request.Content = JsonContent.Create(new KeycloakAdminJson.Role(null, roleName), KeycloakAdminJson.Default.RoleBody);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);

        // Another caller created it between the lookup and here: the role exists, which is what was asked.
        if (response.StatusCode != HttpStatusCode.Conflict)
            response.EnsureSuccessStatusCode();
    }

    public async Task RemoveRealmRoleAsync(string userId, string roleName, CancellationToken ct = default)
    {
        // A role the realm does not have is not mapped to anyone: there is nothing to remove.
        if (await FindRealmRoleAsync(roleName, ct).ConfigureAwait(false) is not { } role)
            return;

        await RoleMappingsAsync(HttpMethod.Delete, userId, role, ct).ConfigureAwait(false);
    }

    /// <summary>The realm role by name, or null when the realm has none by that name.</summary>
    private async Task<KeycloakAdminJson.Role?> FindRealmRoleAsync(string roleName, CancellationToken ct)
    {
        using var request = await AdminRequestAsync(
            HttpMethod.Get, $"/roles/{Uri.EscapeDataString(roleName)}", ct).ConfigureAwait(false);
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(KeycloakAdminJson.Default.RoleBody, ct).ConfigureAwait(false);
    }

    /// <summary>Adds (POST) or removes (DELETE) one realm role in the user's role mappings.</summary>
    private async Task RoleMappingsAsync(HttpMethod method, string userId, KeycloakAdminJson.Role role, CancellationToken ct)
    {
        using var request = await AdminRequestAsync(
            method, $"/users/{Uri.EscapeDataString(userId)}/role-mappings/realm", ct).ConfigureAwait(false);
        request.Content = JsonContent.Create(new[] { role }, KeycloakAdminJson.Default.RolesBody);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpRequestMessage> AdminRequestAsync(HttpMethod method, string path, CancellationToken ct)
    {
        var token = await GetAdminTokenAsync(ct).ConfigureAwait(false);
        var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{_options.Realm}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<string> GetAdminTokenAsync(CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId ?? "",
            ["client_secret"] = _options.ClientSecret ?? ""
        };

        using var content = new FormUrlEncodedContent(form);
        using var response = await http
            .PostAsync($"{_options.Authority}/protocol/openid-connect/token", content, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct).ConfigureAwait(false);
        return token?.AccessToken
               ?? throw new InvalidOperationException("Keycloak admin token response contained no access_token.");
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record UserRepresentation(string Id, string Username);
}
