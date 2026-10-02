namespace Pragmatic.Identity.Local.Permissions;

/// <summary>
///     Permission constants for Identity.Local package.
///     Used in [RequirePermission] on actions and by consumers for role mapping.
/// </summary>
public static class LocalIdentityPermissions
{
    public const string Login = "identity.login";
    public const string Register = "identity.register";
    public const string ChangePassword = "identity.change-password";
    public const string ResetPassword = "identity.reset-password";
}
