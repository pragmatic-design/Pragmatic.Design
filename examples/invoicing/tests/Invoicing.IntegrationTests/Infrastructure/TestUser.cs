namespace Invoicing.IntegrationTests.Infrastructure;

/// <summary>
///     Somebody the identity provider knows: a subject, a display name, and the roles its token carries.
/// </summary>
public sealed record TestUser(string Id, string Name, params string[] Roles);

/// <summary>The people the suite signs in as. The role names are the strings the provider sends.</summary>
public static class TestUsers
{
    public static readonly TestUser PlatformAdministrator =
        new("idp-admin", "Ada Platform", "platform-administrator");

    public static readonly TestUser Accountant = new("idp-accountant", "Bruno Conti", "accountant");

    public static readonly TestUser Viewer = new("idp-viewer", "Carla Vista", "viewer");

    /// <summary>Signed in at the provider, and given no role in this application.</summary>
    public static readonly TestUser WithoutRole = new("idp-stranger", "Dino Nessuno");
}
