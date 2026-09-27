namespace UsersBL;

// نفس القيم بالظبط اللي جوه الـ CHECK constraints في الداتابيز (Users.Users)
public static class UserRoles
{
    public const string Parent = "Parent";
    public const string Child = "Child";
}

public static class AuthProviders
{
    public const string Email = "Email";
    public const string Google = "Google";
    public const string Guest = "Guest";
}
