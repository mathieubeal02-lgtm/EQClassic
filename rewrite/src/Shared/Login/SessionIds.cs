using System;
using System.Globalization;

namespace EQClassic.Shared.Login;

/// <summary>
/// The login server identifies an account to World as "LS#&lt;id&gt;" (OP_SessionId), which World
/// then resolves with account.lsaccount_id.
/// </summary>
public static class SessionIds
{
    private const string Prefix = "LS#";

    public static string ForAccount(int accountId) => Prefix + accountId.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string sessionId, out int accountId)
    {
        accountId = 0;
        return sessionId.StartsWith(Prefix, StringComparison.Ordinal)
            && int.TryParse(sessionId.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out accountId)
            && accountId > 0;
    }
}
