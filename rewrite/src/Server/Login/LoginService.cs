using EQClassic.Server.Accounts;
using EQClassic.Shared.Login;

namespace EQClassic.Server.Login;

/// <summary>
/// The authentication decision of the legacy login server (LS/Login/client.cpp, OP_LoginInfo, and
/// logindatabase.cpp, Database::CheckEQLogin/GetLSLoginInfo), without any networking:
/// <list type="number">
/// <item>name or password of 20 characters or more: bad credentials;</item>
/// <item>unknown name or wrong password: bad credentials (the same message, on purpose);</item>
/// <item>unless lsadmin is 50 or more: not verified, then suspended (lsstatus 40), then banned (lsstatus 50);</item>
/// <item>otherwise success, session id "LS#&lt;id&gt;".</item>
/// </list>
/// </summary>
public sealed class LoginService
{
    private readonly IAccountStore _accounts;

    public LoginService(IAccountStore accounts) => _accounts = accounts;

    public LoginResponse Authenticate(string username, string password) => Authenticate(username, password, out _);

    /// <param name="accountName">On success, the name as stored (legacy strcpy(iUsername, row[1])): what World receives.</param>
    public LoginResponse Authenticate(string username, string password, out string accountName)
    {
        accountName = "";
        if (username.Length > LoginLimits.MaxNameOrPasswordLength || password.Length > LoginLimits.MaxNameOrPasswordLength)
            return LoginResponse.Failure(LoginResult.BadCredentials);

        var account = _accounts.FindByName(username);
        if (account is null || !PasswordHash.Matches(password, account.PasswordSha1))
            return LoginResponse.Failure(LoginResult.BadCredentials);

        if (account.LsAdmin < 50)
        {
            if (!account.Verified)
                return LoginResponse.Failure(LoginResult.NotVerified);
            if (account.LsStatus == 40)
                return LoginResponse.Failure(LoginResult.Suspended);
            if (account.LsStatus == 50)
                return LoginResponse.Failure(LoginResult.Banned);
        }
        accountName = account.Name;
        return LoginResponse.Success(account.Id);
    }
}
