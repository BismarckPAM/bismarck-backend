namespace Messaging;

public static class SecurityEventTypes
{
    public const string UserLogin = "security.auth.login";
    public const string UserLogout = "security.auth.logout";
    public const string PasswordReset = "security.auth.password-reset";
    public const string PermissionChanged = "security.access.permission-changed";
    public const string UnauthorizedAccess = "security.access.unauthorized";
}