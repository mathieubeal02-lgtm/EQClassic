namespace EQClassic.Tests.Accounts;

/// <summary>
/// A test that needs MariaDB: skipped unless EQC_REWRITE_TEST_DB holds a connection string, e.g.
/// "Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None". The tests only
/// create and drop their own temporary table.
/// </summary>
public sealed class DbFactAttribute : FactAttribute
{
    public const string Variable = "EQC_REWRITE_TEST_DB";

    public static string? ConnectionString => Environment.GetEnvironmentVariable(Variable);

    public DbFactAttribute()
    {
        if (string.IsNullOrEmpty(ConnectionString))
            Skip = $"set {Variable} to run MariaDB tests";
    }
}
