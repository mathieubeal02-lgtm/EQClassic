namespace EQClassic.Shared.Login;

/// <summary>
/// Texts shown to the player, identical to the current login server (LS/Login/client.h) so the
/// rewrite reports failures exactly like the Trilogy-era server does.
/// </summary>
public static class LoginMessages
{
    public const string WorldNotFound = "Worldserver not found.";
    public const string WorldDown = "World is down.";
    public const string WorldLocked = "World is locked.";
    public const string BadCredentials = "Incorrect username or password.";
    public const string AccountBanned = "This account has been BANNED. To contest this decision, please contact the server's administration.";
    public const string AccountSuspended = "This account has been SUSPENDED. To contest this decision, please contact the server's administration.";
    public const string AccountNotVerified = "Your account has not been verified by e-mail.  Please goto http://www.eqemulator.org/ to update your information.";
    public const string Malformed = "Malformed OP_LoginInfo";

    public static string For(LoginResult result) => result switch
    {
        LoginResult.Success => "",
        LoginResult.BadCredentials => BadCredentials,
        LoginResult.NotVerified => AccountNotVerified,
        LoginResult.Suspended => AccountSuspended,
        LoginResult.Banned => AccountBanned,
        LoginResult.Malformed => Malformed,
        _ => "Unknown Error",
    };

    /// <summary>The legacy server prefixes every fatal error with "Error: " (Client::FatalError).</summary>
    public static string AsFatalError(string message) => message.Length > 1 ? "Error: " + message : "Unknown Error";
}
