using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;

namespace EQClassic.Tests.Login;

/// <summary>Parity with the legacy login decision (LS/Login/client.cpp OP_LoginInfo, logindatabase.cpp).</summary>
public class LoginServiceTests
{
    private static LoginService ServiceWith(params LoginAccount[] accounts) => new(new InMemoryAccountStore(accounts));
    private static LoginAccount Account(int id, string name, string password, int lsAdmin = 0, int lsStatus = 0, bool verified = true) =>
        new(id, name, PasswordHash.Sha1Hex(password), lsAdmin, lsStatus, verified);

    [Fact]
    public void Test_account_logs_in_with_LS_session_id()
    {
        var result = new LoginService(InMemoryAccountStore.WithTestAccount()).Authenticate("test", "test");

        Assert.Equal(LoginResult.Success, result.Result);
        Assert.Equal("LS#1", result.SessionId);
        Assert.Equal(1, result.AccountId);
        Assert.Equal("", result.Message);
    }

    [Fact]
    public void Wrong_password_is_rejected_with_the_legacy_message()
    {
        var result = ServiceWith(Account(1, "test", "test")).Authenticate("test", "nope");

        Assert.Equal(LoginResult.BadCredentials, result.Result);
        Assert.Equal("Incorrect username or password.", result.Message);
        Assert.Equal("", result.SessionId);
    }

    [Fact]
    public void Unknown_account_gets_the_same_message_as_a_wrong_password()
    {
        var service = ServiceWith(Account(1, "test", "test"));
        Assert.Equal(service.Authenticate("test", "nope"), service.Authenticate("ghost", "test"));
    }

    [Fact]
    public void Account_name_is_case_insensitive_like_the_mysql_query()
    {
        Assert.Equal(LoginResult.Success, ServiceWith(Account(7, "Test", "test")).Authenticate("TEST", "test").Result);
    }

    [Fact]
    public void Password_is_case_sensitive()
    {
        Assert.Equal(LoginResult.BadCredentials, ServiceWith(Account(1, "test", "Secret")).Authenticate("test", "secret").Result);
    }

    [Theory]
    [InlineData("12345678901234567890", "test")] // 20 characters: does not fit username[20] with its NUL
    [InlineData("test", "12345678901234567890")]
    public void Names_or_passwords_of_20_characters_are_bad_credentials(string user, string password)
    {
        var service = ServiceWith(Account(1, user, password));
        Assert.Equal(LoginResult.BadCredentials, service.Authenticate(user, password).Result);
    }

    [Fact]
    public void Nineteen_characters_are_accepted()
    {
        var name = new string('a', 19);
        Assert.Equal(LoginResult.Success, ServiceWith(Account(1, name, name)).Authenticate(name, name).Result);
    }

    [Fact]
    public void Unverified_account_is_refused()
    {
        var result = ServiceWith(Account(1, "test", "test", verified: false)).Authenticate("test", "test");
        Assert.Equal(LoginResult.NotVerified, result.Result);
        Assert.Equal(LoginMessages.AccountNotVerified, result.Message);
    }

    [Theory]
    [InlineData(40, LoginResult.Suspended)]
    [InlineData(50, LoginResult.Banned)]
    public void Suspended_and_banned_accounts_are_refused(int lsStatus, LoginResult expected)
    {
        var result = ServiceWith(Account(1, "test", "test", lsStatus: lsStatus)).Authenticate("test", "test");
        Assert.Equal(expected, result.Result);
        Assert.Equal(LoginMessages.For(expected), result.Message);
    }

    [Fact]
    public void Verification_is_checked_before_suspension()
    {
        var result = ServiceWith(Account(1, "test", "test", lsStatus: 50, verified: false)).Authenticate("test", "test");
        Assert.Equal(LoginResult.NotVerified, result.Result);
    }

    [Theory]
    [InlineData(40, false)]
    [InlineData(50, true)]
    public void Login_admins_bypass_the_status_checks(int lsStatus, bool verified)
    {
        var result = ServiceWith(Account(3, "gm", "gm", lsAdmin: 50, lsStatus: lsStatus, verified: verified)).Authenticate("gm", "gm");
        Assert.Equal(LoginResult.Success, result.Result);
        Assert.Equal("LS#3", result.SessionId);
    }

    [Fact]
    public void Other_status_values_do_not_block()
    {
        Assert.Equal(LoginResult.Success, ServiceWith(Account(1, "test", "test", lsStatus: 10)).Authenticate("test", "test").Result);
    }
}
