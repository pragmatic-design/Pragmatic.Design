using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Pragmatic.Identity.Keycloak;

/// <summary>
///     The admin API's request and response bodies, serialized by generated code rather than by reflection.
/// </summary>
/// <remarks>
///     Null members are left out: a user created without attributes sends no <c>attributes</c> at all,
///     rather than a <c>null</c> Keycloak would have to read as "none".
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KeycloakAdminJson.User), TypeInfoPropertyName = "UserBody")]
[JsonSerializable(typeof(KeycloakAdminJson.Credential), TypeInfoPropertyName = "CredentialBody")]
[JsonSerializable(typeof(KeycloakAdminJson.Role), TypeInfoPropertyName = "RoleBody")]
[JsonSerializable(typeof(KeycloakAdminJson.Role[]), TypeInfoPropertyName = "RolesBody")]
internal sealed partial class KeycloakAdminJson : JsonSerializerContext
{
    /// <summary>A user as <c>POST /users</c> takes it.</summary>
    internal sealed record User(
        string Username,
        string? Email,
        string? FirstName,
        string? LastName,
        bool Enabled,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? Attributes);

    /// <summary>A credential as <c>PUT /users/{id}/reset-password</c> takes it.</summary>
    internal sealed record Credential(string Type, string Value, bool Temporary);

    /// <summary>A realm role: what <c>GET /roles/{name}</c> answers and the role mappings take.</summary>
    internal sealed record Role(string? Id, string Name);
}
