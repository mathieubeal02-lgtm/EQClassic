namespace EQClassic.Shared.Login;

/// <summary>Field limits inherited from the Trilogy client (LoginCrypt_struct, SessionId_Struct).</summary>
public static class LoginLimits
{
    /// <summary>
    /// username[20] and password[20] in the client's credential block: the legacy server rejects
    /// anything of 20 characters or more (Database::CheckEQLogin) as bad credentials.
    /// </summary>
    public const int MaxNameOrPasswordLength = 19;

    /// <summary>Length of the key World uses to accept a player from the login server.</summary>
    public const int SessionKeyLength = 15;
}
