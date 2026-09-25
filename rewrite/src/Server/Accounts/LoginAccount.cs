namespace EQClassic.Server.Accounts;

/// <summary>
/// A row of the legacy login_accounts table (sql/schema.sql), the fields the login decision reads.
/// </summary>
/// <param name="PasswordSha1">Hex SHA-1 of the password, as MySQL's SHA() stores it.</param>
/// <param name="LsAdmin">50 or more bypasses the verified/suspended/banned checks.</param>
/// <param name="LsStatus">40 = suspended, 50 = banned.</param>
/// <param name="Verified">login_accounts.user_active != 0.</param>
public sealed record LoginAccount(
    int Id,
    string Name,
    string PasswordSha1,
    int LsAdmin = 0,
    int LsStatus = 0,
    bool Verified = true,
    int WorldAdmin = 0);
