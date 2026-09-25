namespace EQClassic.Server.Accounts;

public interface IAccountStore
{
    /// <summary>
    /// Finds an account by name. Like the legacy query (<c>name = '...'</c> under MySQL's
    /// case-insensitive latin1 collation), the match ignores case.
    /// </summary>
    LoginAccount? FindByName(string name);
}

/// <summary>Accounts kept in memory: tests, and the server until the database store exists.</summary>
public sealed class InMemoryAccountStore : IAccountStore
{
    private readonly Dictionary<string, LoginAccount> _byName = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryAccountStore(IEnumerable<LoginAccount>? accounts = null)
    {
        foreach (var account in accounts ?? [])
            Add(account);
    }

    public void Add(LoginAccount account) => _byName[account.Name] = account;

    public LoginAccount? FindByName(string name) => _byName.GetValueOrDefault(name);

    /// <summary>The runbook's test account (docs/RUNBOOK.md): test / test, verified, id 1.</summary>
    public static InMemoryAccountStore WithTestAccount() =>
        new([new LoginAccount(1, "test", PasswordHash.Sha1Hex("test"))]);
}
