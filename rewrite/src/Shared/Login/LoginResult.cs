namespace EQClassic.Shared.Login;

/// <summary>Outcome of an authentication attempt, one value per error path of LS/Login/client.cpp (OP_LoginInfo).</summary>
public enum LoginResult : byte
{
    Success = 0,
    /// <summary>Unknown account, wrong password, or a name/password of 20 characters or more.</summary>
    BadCredentials = 1,
    /// <summary>login_accounts.user_active = 0 (and lsadmin below 50).</summary>
    NotVerified = 2,
    /// <summary>lsstatus = 40 (and lsadmin below 50).</summary>
    Suspended = 3,
    /// <summary>lsstatus = 50 (and lsadmin below 50).</summary>
    Banned = 4,
    /// <summary>The request itself could not be read.</summary>
    Malformed = 5,
}
